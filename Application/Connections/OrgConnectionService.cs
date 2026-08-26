using Application.Bindings;
using Database.Models;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Salesforce;
using Salesforce.Auth;
using System.ComponentModel.DataAnnotations;

namespace Application.Connections;

/// <summary>
/// Setting up, verifying and tearing down the Org Connection.
/// </summary>
/// <remarks>
/// Owns the whole user-facing lifecycle. Connection State itself turns in
/// <see cref="StoredOrgConnectionSource"/>, because it follows the outcome of a token request and the token
/// provider is what makes those.
/// </remarks>
public interface IOrgConnectionService {
    Task<OrgConnectionDTO> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores connection details and generates a Signing Keypair. Leaves the connection Incomplete.</summary>
    Task<OrgConnectionDTO> SaveAsync(SaveOrgConnectionDTO request, CancellationToken cancellationToken = default);

    /// <summary>Builds the Salesforce authorize URL for the Administering User's browser.</summary>
    Task<BootstrapStartDTO> StartBootstrapAsync(CancellationToken cancellationToken = default);

    /// <summary>Handles the OAuth callback: verifies state, exchanges the code, configures, then verifies.</summary>
    Task<OrgConnectionDTO> CompleteBootstrapAsync(string code, string state, CancellationToken cancellationToken = default);

    /// <summary>Requests a JWT token for both identities, recording the outcome on the connection.</summary>
    Task<OrgConnectionDTO> VerifyAsync(CancellationToken cancellationToken = default);

    /// <summary>What a Disconnect would destroy.</summary>
    Task<DisconnectPreviewDTO> PreviewDisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Wipes the connection and every piece of org-specific state. Nothing in Salesforce is touched.</summary>
    Task<DisconnectPreviewDTO> DisconnectAsync(ConfirmDisconnectDTO confirmation, CancellationToken cancellationToken = default);

    /// <summary>The Signing Certificate as PEM, for a user registering it in Setup by hand.</summary>
    Task<string> GetCertificatePemAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OrgConnectionService : IOrgConnectionService {
    private readonly IOrgConnectionRepository _repository;
    private readonly IOrgConnectionProvider _provider;
    private readonly ISecretProtector _protector;
    private readonly ISalesforceTokenProvider _tokenProvider;
    private readonly IBootstrapOAuthClient _bootstrap;
    private readonly IBootstrapStateStore _stateStore;
    private readonly IOrgSelfConfigurator _selfConfigurator;
    private readonly IConfigurationChangeSignal _changeSignal;
    private readonly SalesforceConfig _config;
    private readonly ILogger<OrgConnectionService> _logger;
    private readonly TimeProvider _time;

    public OrgConnectionService(IOrgConnectionRepository repository, IOrgConnectionProvider provider,
        ISecretProtector protector, ISalesforceTokenProvider tokenProvider, IBootstrapOAuthClient bootstrap,
        IBootstrapStateStore stateStore, IOrgSelfConfigurator selfConfigurator,
        IConfigurationChangeSignal changeSignal, IOptions<SalesforceConfig> config,
        ILogger<OrgConnectionService> logger, TimeProvider time) {
        _repository = repository;
        _provider = provider;
        _protector = protector;
        _tokenProvider = tokenProvider;
        _bootstrap = bootstrap;
        _stateStore = stateStore;
        _selfConfigurator = selfConfigurator;
        _changeSignal = changeSignal;
        _config = config.Value;
        _logger = logger;
        _time = time;
    }

    public async Task<OrgConnectionDTO> GetAsync(CancellationToken cancellationToken = default) {
        var connection = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(connection);
    }

    public async Task<OrgConnectionDTO> SaveAsync(SaveOrgConnectionDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        Validate(request);
        RequireCallbackUrl();

        if (!_protector.IsAvailable) {
            // Refused rather than stored in the clear. An application that protects nothing while claiming to
            // is worse than one that says it cannot yet.
            throw new ValidationException(
                "This application cannot store a credential because no protecting key is available. Supply a " +
                "protecting certificate through DataProtection:ProtectingCertificate and restart. Looked at: " +
                $"{_protector.ProtectingKeyDescription}.");
        }

        var existing = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);

        // Editing a Connected connection is allowed on purpose. It is the scenario the org-mismatch refusal
        // exists for: a user re-points the Run-as User at another org's user, verification discovers a
        // different org id, and refuses. Blocking the edit here would make that guard unreachable and turn a
        // clear refusal into a vaguer "Disconnect first" for edits that are perfectly legitimate.
        //
        // The keypair is reused when one already exists. Regenerating it would silently invalidate the
        // certificate Salesforce holds, so correcting a typo in a username would break authentication and
        // require re-registering the certificate by hand.
        var keypair = existing is null ? SigningKeypairFactory.Create(_time.GetUtcNow()) : null;

        var saved = await _repository.UpsertAsync(new OrgConnection {
            ConsumerKey = request.ConsumerKey.Trim(),
            AdministeringUsername = request.AdministeringUsername.Trim(),
            RunAsUsername = request.RunAsUsername.Trim(),
            IsSandbox = request.IsSandbox,
            SigningPrivateKey = keypair is null ? existing!.SigningPrivateKey : _protector.Protect(keypair.PrivateKeyPem),
            SigningCertificate = keypair?.CertificatePem ?? existing!.SigningCertificate,
            CertificateFingerprint = keypair?.Fingerprint ?? existing!.CertificateFingerprint,
            CertificateExpiresAt = keypair?.ExpiresAt ?? existing!.CertificateExpiresAt
        }, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.ConsumerSecret)) {
            await _repository.SaveBootstrapSecretsAsync(
                _protector.Protect(request.ConsumerSecret.Trim()), null, cancellationToken).ConfigureAwait(false);
        }

        _provider.Invalidate();
        _tokenProvider.ClearCache();
        _changeSignal.Signal();

        _logger.LogInformation(
            "Org Connection saved for Consumer Key {ConsumerKey}; Signing Certificate {Fingerprint} expires {Expiry:u}",
            saved.ConsumerKey, saved.CertificateFingerprint, saved.CertificateExpiresAt);

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BootstrapStartDTO> StartBootstrapAsync(CancellationToken cancellationToken = default) {
        var connection = await RequireConnectionAsync(cancellationToken).ConfigureAwait(false);
        var callbackUrl = RequireCallbackUrl();

        if (connection.BootstrapConsumerSecret is null) {
            throw new ValidationException(
                "The Consumer Secret is needed for the one-time browser approval. Save it with the connection " +
                "details first; it is discarded once the connection works.");
        }

        var state = _stateStore.Issue();

        return new BootstrapStartDTO {
            AuthorizeUrl = _bootstrap.BuildAuthorizeUrl(
                SalesforceLoginHost.For(connection.IsSandbox), connection.ConsumerKey, callbackUrl, state)
        };
    }

    public async Task<OrgConnectionDTO> CompleteBootstrapAsync(string code, string state,
        CancellationToken cancellationToken = default) {
        if (!_stateStore.TryConsume(state)) {
            // Not "try again": an unrecognised state is either a replay or a callback this application never
            // started, and neither should be exchanged for a token.
            throw new ValidationException(
                "This authorization callback does not match a Bootstrap this application started, or it has " +
                "expired. Start the connection again from the setup screen.");
        }

        if (string.IsNullOrWhiteSpace(code)) {
            throw new ValidationException("Salesforce returned no authorization code.");
        }

        var connection = await RequireConnectionAsync(cancellationToken).ConfigureAwait(false);
        var callbackUrl = RequireCallbackUrl();

        if (connection.BootstrapConsumerSecret is null) {
            throw new ValidationException(
                "The Consumer Secret needed to complete the Bootstrap is not stored. Save the connection " +
                "details again, then retry.");
        }

        var consumerSecret = _protector.Unprotect(connection.BootstrapConsumerSecret);

        var session = await _bootstrap.ExchangeCodeAsync(
            SalesforceLoginHost.For(connection.IsSandbox), connection.ConsumerKey, consumerSecret, callbackUrl,
            code, cancellationToken).ConfigureAwait(false);

        // Kept until the first JWT succeeds, not until the deploy returns. A metadata deploy is eventually
        // consistent, so "configured, JWT not working yet" is expected — and this session is the only way to
        // retry without sending the user back through Setup.
        await _repository.SaveBootstrapSecretsAsync(
            connection.BootstrapConsumerSecret,
            session.RefreshToken is null ? null : _protector.Protect(session.RefreshToken),
            cancellationToken).ConfigureAwait(false);
        _provider.Invalidate();

        var configuration = await _selfConfigurator.ConfigureAsync(
            session.AccessToken!, session.InstanceUrl!, connection.SigningCertificate, cancellationToken)
            .ConfigureAwait(false);

        if (configuration.Configured) {
            _logger.LogInformation("Self-Configuration complete: {Summary}", configuration.Summary);
        } else {
            _logger.LogWarning("Self-Configuration did not run: {Summary}", configuration.Summary);
            foreach (var step in configuration.ManualSteps) {
                _logger.LogWarning("Manual Registration step: {Step}", step);
            }
        }

        return await VerifyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrgConnectionDTO> VerifyAsync(CancellationToken cancellationToken = default) {
        await RequireConnectionAsync(cancellationToken).ConfigureAwait(false);

        _tokenProvider.ClearCache();

        try {
            // Both identities, because both are used in normal operation and both need pre-authorization.
            // Verifying only one leaves the other to fail later, during work the user is not watching.
            await _tokenProvider.ForceRefreshAsync(SalesforceIdentity.RunAsUser, cancellationToken).ConfigureAwait(false);
            await _tokenProvider.ForceRefreshAsync(SalesforceIdentity.AdministeringUser, cancellationToken).ConfigureAwait(false);

            // Only now is the borrowed session surplus.
            await _repository.PurgeBootstrapSecretsAsync(cancellationToken).ConfigureAwait(false);
            _provider.Invalidate();
            _changeSignal.Signal();
        } catch (SalesforceOAuthException) {
            // Already recorded onto the connection by the token provider, and the read model carries the raw
            // Salesforce text plus any translation. Rethrowing here would say the same thing twice.
            _provider.Invalidate();
        }

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DisconnectPreviewDTO> PreviewDisconnectAsync(CancellationToken cancellationToken = default) {
        var counts = await _repository.CountOrgScopedStateAsync(cancellationToken).ConfigureAwait(false);
        return ToPreview(counts);
    }

    public async Task<DisconnectPreviewDTO> DisconnectAsync(ConfirmDisconnectDTO confirmation,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(confirmation);

        var counts = await _repository.CountOrgScopedStateAsync(cancellationToken).ConfigureAwait(false);

        // The confirmation names counts so a caller that has not looked at what it is destroying cannot
        // satisfy it by accident, and so a Binding created since the preview aborts rather than vanishes.
        if (confirmation.ExpectedBindings != counts.Bindings ||
            confirmation.ExpectedFieldMappings != counts.FieldMappings) {
            throw new ValidationException(
                $"Disconnect was confirmed for {confirmation.ExpectedBindings} Binding(s) and " +
                $"{confirmation.ExpectedFieldMappings} Field Mapping(s), but there are now {counts.Bindings} " +
                $"and {counts.FieldMappings}. Nothing has been destroyed. Review the preview and confirm again.");
        }

        var destroyed = await _repository.DeleteConnectionAndOrgScopedStateAsync(cancellationToken).ConfigureAwait(false);

        _provider.Invalidate();
        _tokenProvider.ClearCache();
        _changeSignal.Signal();

        return ToPreview(destroyed);
    }

    public async Task<string> GetCertificatePemAsync(CancellationToken cancellationToken = default) {
        var connection = await RequireConnectionAsync(cancellationToken).ConfigureAwait(false);
        return connection.SigningCertificate;
    }

    private async Task<OrgConnection> RequireConnectionAsync(CancellationToken cancellationToken) {
        return await _provider.GetAsync(cancellationToken).ConfigureAwait(false)
               ?? throw new KeyNotFoundException(
                   "No Org Connection has been set up. Supply the connection details first.");
    }

    private static void Validate(SaveOrgConnectionDTO request) {
        if (string.IsNullOrWhiteSpace(request.ConsumerKey)) {
            throw new ValidationException("The Consumer Key is required. Copy it from the External Client App in Setup.");
        }
        if (string.IsNullOrWhiteSpace(request.AdministeringUsername)) {
            throw new ValidationException("The Administering User's username is required.");
        }
        if (string.IsNullOrWhiteSpace(request.RunAsUsername)) {
            throw new ValidationException("The Run-as User's username is required.");
        }
    }

    private string RequireCallbackUrl() {
        if (_config.ValidateCallbackUrl() is { } problem) {
            throw new ValidationException(problem);
        }

        return _config.CallbackUrl!;
    }

    private static DisconnectPreviewDTO ToPreview(OrgScopedStateCounts counts) => new() {
        Bindings = counts.Bindings,
        FieldMappings = counts.FieldMappings,
        AvroSchemas = counts.AvroSchemas,
        Channels = counts.Channels,
        ChannelMembers = counts.ChannelMembers,
        LeftInSalesforce = [
            "The External Client App you created, along with its Consumer Key and Secret",
            "The permission set granting access to it, and its assignment to the Run-as User",
            "The Signing Certificate registered on the app"
        ]
    };

    private OrgConnectionDTO ToDto(OrgConnection? connection) {
        var secretProtection = DescribeSecretProtection(connection);

        if (connection is null) {
            return new OrgConnectionDTO {
                Exists = false,
                CallbackUrl = _config.CallbackUrl ?? "",
                SecretProtection = secretProtection
            };
        }

        return new OrgConnectionDTO {
            Exists = true,
            ConnectionState = connection.ConnectionState.ToString(),
            ConsumerKey = connection.ConsumerKey,
            AdministeringUsername = connection.AdministeringUsername,
            RunAsUsername = connection.RunAsUsername,
            IsSandbox = connection.IsSandbox,
            OrgUrl = connection.OrgUrl,
            OrgId = connection.OrgId,
            LastConnectedAt = connection.LastConnectedAt,
            LastError = BuildLastError(connection),
            CertificateFingerprint = connection.CertificateFingerprint,
            CertificateExpiresAt = connection.CertificateExpiresAt,
            CallbackUrl = _config.CallbackUrl ?? "",
            HasBootstrapSession = connection.BootstrapConsumerSecret is not null,
            SecretProtection = secretProtection
        };
    }

    private static OAuthFailureDTO? BuildLastError(OrgConnection connection) {
        if (string.IsNullOrWhiteSpace(connection.LastError)) {
            return null;
        }

        // Both halves reach the user: the translated summary, and Salesforce's own words underneath it. The
        // raw text is stored separately precisely so this does not have to reconstruct it from a summary.
        return new OAuthFailureDTO {
            Error = connection.ConnectionState.ToString(),
            ErrorDescription = connection.LastError,
            RawResponse = connection.LastErrorRaw ?? "",
            OccurredAt = connection.LastErrorAt
        };
    }

    private SecretProtectionDTO DescribeSecretProtection(OrgConnection? connection) {
        if (_protector.IsAvailable) {
            // A stored key that will not decrypt is the case that must never read as "not configured yet".
            if (connection is not null && !_protector.TryUnprotect(connection.SigningPrivateKey, out _)) {
                return new SecretProtectionDTO {
                    Status = "Unreadable",
                    ProtectingKey = _protector.ProtectingKeyDescription,
                    Guidance =
                        "A Signing Keypair is stored but cannot be decrypted with the protecting key that is " +
                        "present. Restore the original protecting certificate and key ring. Do not re-run " +
                        "setup: it would generate a new keypair, orphan the External Client App in Salesforce, " +
                        "and hide the fact that a key went missing."
                };
            }

            return new SecretProtectionDTO {
                Status = "Ready",
                ProtectingKey = _protector.ProtectingKeyDescription
            };
        }

        return new SecretProtectionDTO {
            Status = connection is null ? "NotConfigured" : "Unreadable",
            ProtectingKey = _protector.ProtectingKeyDescription,
            Guidance = connection is null
                ? "No protecting certificate is configured, so no credential can be stored yet. Supply one " +
                  "through DataProtection:ProtectingCertificate and restart."
                : "A credential is stored but no protecting key is available to read it. Restore the " +
                  "protecting certificate rather than re-running setup."
        };
    }
}
