using Application.Bindings;
using Application.Connections;
using Application.Mappers;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using Database.Writeback;
using DTO;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Application.Targets;

/// <summary>
/// Thrown when a save would change which database the Target Connection points at.
/// </summary>
/// <remarks>
/// A conflict rather than a validation failure: the request was well formed, and the refusal is about what
/// it would destroy. Repointing is its own operation, with a preview and a confirmation, and the API maps
/// this to a response that says so.
/// </remarks>
public sealed class TargetConnectionIdentityChangedException : InvalidOperationException {
    public TargetConnectionIdentityChangedException(TargetConnectionIdentity stored, TargetConnectionIdentity requested)
        : base("These details point at a different database " +
               $"({Describe(requested)} rather than {Describe(stored)}), which would invalidate every Binding. " +
               "Use the repoint operation, which previews what will be destroyed and asks for confirmation.") { }

    private static string Describe(TargetConnectionIdentity identity) =>
        identity.FilePath is { } file ? $"{identity.Engine} {file}" : $"{identity.Engine} {identity.Host}/{identity.DatabaseName}";
}

/// <summary>
/// Thrown when a repoint's new database fails its proof. Nothing has been destroyed.
/// </summary>
/// <remarks>
/// Carries the summary and the driver's message separately, as the stored last error does, so the API can
/// return both and the page can show the summary with the searchable text behind it.
/// </remarks>
public sealed class TargetConnectionProofFailedException : InvalidOperationException {
    public TargetConnectionProofFailedException(string message, string rawResponse, Exception? innerException = null)
        : base(message, innerException) {
        RawResponse = rawResponse;
    }

    /// <summary>The driver's message, untouched.</summary>
    public string RawResponse { get; }
}

/// <summary>
/// Setting up, proving, editing and repointing the Target Connection.
/// </summary>
/// <remarks>
/// Owns the whole user-facing lifecycle. The one state change that happens elsewhere is the worker recording
/// a write failure or a recovery, because the worker is where the connection is genuinely exercised.
/// </remarks>
public interface ITargetConnectionService {
    Task<TargetConnectionDTO> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Every engine, with the fields it asks for and whether it can be used. What a form renders from.</summary>
    IReadOnlyList<EngineDefinitionDTO> GetEngines();

    /// <summary>
    /// Stores the details and proves them. Creates the Target Connection, or edits the existing one
    /// non-destructively.
    /// </summary>
    /// <remarks>
    /// Always persists. A proof that fails leaves the connection Incomplete with the error — a user whose
    /// database is briefly unreachable must still be able to record what they typed.
    /// <para>
    /// A change of identity is accepted only while no Binding exists; with any, it is a repoint. A blank
    /// password on an edit with an unchanged identity keeps the stored one.
    /// </para>
    /// </remarks>
    Task<TargetConnectionDTO> SaveAsync(SaveTargetConnectionDTO request, CancellationToken cancellationToken = default);

    /// <summary>Proves the stored Target Connection again and records the outcome. The user's repair button.</summary>
    Task<TargetConnectionDTO> VerifyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a write to the Target Database failed, moving the connection to Failed.
    /// </summary>
    /// <remarks>
    /// Called by the worker, which is where the connection is genuinely exercised. Does not raise the change
    /// signal: the worker is the thing that would be woken, and it already knows.
    /// </remarks>
    Task RecordWriteFailureAsync(Exception failure, CancellationToken cancellationToken = default);

    /// <summary>What a repoint would destroy. Call this before confirming one.</summary>
    Task<RepointPreviewDTO> PreviewRepointAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points the application at a different database, destroying every Binding.
    /// </summary>
    /// <remarks>
    /// All or nothing, unlike save: the new details are proved <em>before</em> anything is destroyed, because
    /// destroying Bindings and then discovering the new database is unreachable leaves the user with neither.
    /// </remarks>
    Task<TargetConnectionDTO> RepointAsync(RepointTargetConnectionDTO request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the Replication Credential and proves it: it logs in to the same database, may read its changes,
    /// and the server can do Two-way. Stored either way, with the outcome. A credential edit never Repoints.
    /// </summary>
    Task<TargetConnectionDTO> SetReplicationCredentialAsync(SetReplicationCredentialDTO request, CancellationToken cancellationToken = default);

    /// <summary>Proves the stored Replication Credential again and records the outcome.</summary>
    Task<TargetConnectionDTO> VerifyReplicationCredentialAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the Replication Credential; Two-way Bindings stop writing back until another is entered.</summary>
    Task<TargetConnectionDTO> ClearReplicationCredentialAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TargetConnectionService : ITargetConnectionService {
    private readonly ITargetConnectionRepository _repository;
    private readonly ITargetConnectionProvider _provider;
    private readonly ITargetEngineCatalog _engines;
    private readonly ISecretProtector _protector;
    private readonly IConfigurationChangeSignal _changeSignal;
    private readonly ILogger<TargetConnectionService> _logger;
    private readonly TimeProvider _time;

    public TargetConnectionService(ITargetConnectionRepository repository, ITargetConnectionProvider provider,
        ITargetEngineCatalog engines, ISecretProtector protector, IConfigurationChangeSignal changeSignal,
        ILogger<TargetConnectionService> logger, TimeProvider time) {
        _repository = repository;
        _provider = provider;
        _engines = engines;
        _protector = protector;
        _changeSignal = changeSignal;
        _logger = logger;
        _time = time;
    }

    public async Task<TargetConnectionDTO> GetAsync(CancellationToken cancellationToken = default) {
        var connection = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        return connection.ToDto(DescribeSecretProtection(connection));
    }

    public IReadOnlyList<EngineDefinitionDTO> GetEngines() =>
        _engines.All.Select(profile => profile.ToDto()).ToList();

    public async Task<TargetConnectionDTO> SaveAsync(SaveTargetConnectionDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        var (profile, details, keptPassword) = Validate(request, existing);
        var identityChanged = existing is not null
            && existing.Identity != new TargetConnectionIdentity(details.Engine, details.Host, details.DatabaseName, details.FilePath);

        // An early answer, so a refused identity change is the conflict it is rather than whichever field the
        // profile finds missing first (a blank password, typically). The check inside the upsert stays the one
        // that counts.
        if (identityChanged && (await _repository.CountBindingsAsync(cancellationToken).ConfigureAwait(false)).Bindings > 0) {
            throw new TargetConnectionIdentityChangedException(existing!.Identity, details.ToModel(null).Identity);
        }

        ThrowIfInvalid(profile, details);
        var model = details.ToModel(keptPassword ?? Protect(details.Password));

        if (identityChanged) {
            // Without Bindings there is nothing a different database could silently break, so this is an
            // edit. With any, it is a repoint, which previews what it destroys. See docs/adr/0004.
            _ = await _repository.UpsertUnlessBoundAsync(model, cancellationToken).ConfigureAwait(false)
                ?? throw new TargetConnectionIdentityChangedException(existing.Identity, model.Identity);
        } else {
            await _repository.UpsertAsync(model, cancellationToken).ConfigureAwait(false);
        }
        _provider.Invalidate();

        // The upsert reset the row to Incomplete, so a failed proof here stays Incomplete: what was just
        // typed has never worked, whatever the previous details did.
        await ProveAsync(profile, details, ConnectionState.Incomplete, cancellationToken).ConfigureAwait(false);

        _provider.Invalidate();
        _changeSignal.Signal();

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TargetConnectionDTO> VerifyAsync(CancellationToken cancellationToken = default) {
        var stored = await _provider.GetAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new NoTargetDatabaseException();

        var profile = _engines.For(stored.Engine);
        await ProveAsync(profile, stored.Decrypt(_protector), stored.ConnectionState, cancellationToken).ConfigureAwait(false);

        _provider.Invalidate();
        _changeSignal.Signal();

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordWriteFailureAsync(Exception failure, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(failure);

        await _repository.RecordFailureAsync(Summarise(failure), failure.Message, _time.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);
        _provider.Invalidate();
    }

    public async Task<RepointPreviewDTO> PreviewRepointAsync(CancellationToken cancellationToken = default) {
        var counts = await _repository.CountBindingsAsync(cancellationToken).ConfigureAwait(false);
        return counts.ToPreviewDto();
    }

    public async Task<TargetConnectionDTO> RepointAsync(RepointTargetConnectionDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await _provider.GetAsync(cancellationToken).ConfigureAwait(false)
                       ?? throw new NoTargetDatabaseException();

        var (profile, details, _) = Validate(request, existing: null);
        ThrowIfInvalid(profile, details);
        var model = details.ToModel(Protect(details.Password));

        if (existing.Identity == model.Identity) {
            throw new ValidationException(
                "These details point at the same database as the current Target Connection, so there is nothing " +
                "to repoint to and no reason to destroy any Binding. Use save to edit credentials or options.");
        }

        // The confirmation names counts so a caller that has not looked at what it is destroying cannot
        // satisfy it by accident, and so a Binding created since the preview aborts rather than vanishes.
        var counts = await _repository.CountBindingsAsync(cancellationToken).ConfigureAwait(false);
        if (request.ExpectedBindings != counts.Bindings || request.ExpectedFieldMappings != counts.FieldMappings) {
            throw new ValidationException(
                $"The repoint was confirmed for {request.ExpectedBindings} Binding(s) and " +
                $"{request.ExpectedFieldMappings} Field Mapping(s), but there are now {counts.Bindings} " +
                $"and {counts.FieldMappings}. Nothing has been destroyed. Review the preview and confirm again.");
        }

        try {
            await ReadSchemaAsync(profile, details, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            throw new TargetConnectionProofFailedException($"{Summarise(ex)} Nothing has been destroyed.", ex.Message, ex);
        }

        // Taken down while the old database is still the Target Connection, so its replication slot does not keep
        // changes forever for a reader that is about to be pointed elsewhere.
        await _provider.DisarmWritebackAsync(cancellationToken).ConfigureAwait(false);

        var (_, destroyed) = await _repository.RepointAsync(model, _time.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);

        _provider.Invalidate();
        _changeSignal.Signal();

        _logger.LogWarning("Repointed the Target Connection; {Bindings} Binding(s) and {Mappings} Field Mapping(s) were destroyed",
            destroyed.Bindings, destroyed.FieldMappings);

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TargetConnectionDTO> SetReplicationCredentialAsync(SetReplicationCredentialDTO request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _provider.GetAsync(cancellationToken).ConfigureAwait(false) ?? throw new NoTargetDatabaseException();
        var (profile, writeback) = RequireWriteback(stored);

        var username = request.Username?.Trim();
        if (string.IsNullOrEmpty(username)) {
            throw new ValidationException("The Replication Credential needs a username.");
        }
        if (!_protector.IsAvailable) {
            throw new ValidationException(
                "This application cannot store a password because no protecting key is available. " +
                $"Supply a protecting certificate through DataProtection:ProtectingCertificate and restart. Looked at: {_protector.ProtectingKeyDescription}.");
        }

        string password;
        if (!string.IsNullOrEmpty(request.Password)) {
            password = request.Password;
        } else if (username == stored.ReplicationUsername && stored.ReplicationPasswordEncrypted is { } kept) {
            password = _protector.Unprotect(kept);
        } else {
            throw new ValidationException("The Replication Credential needs a password.");
        }

        await _repository.SetReplicationCredentialAsync(username, Protect(password), cancellationToken).ConfigureAwait(false);
        _provider.Invalidate();

        // Never proved yet, so a failed proof leaves it Incomplete rather than Failed.
        await ProveReplicationAsync(profile, writeback, ReplicationDetails(stored, username, password),
            ConnectionState.Incomplete, cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TargetConnectionDTO> VerifyReplicationCredentialAsync(CancellationToken cancellationToken = default) {
        var stored = await _provider.GetAsync(cancellationToken).ConfigureAwait(false) ?? throw new NoTargetDatabaseException();
        var (profile, writeback) = RequireWriteback(stored);
        if (!stored.HasReplicationCredential) {
            throw new ValidationException("No Replication Credential has been entered, so there is nothing to verify.");
        }

        var details = ReplicationDetails(stored, stored.ReplicationUsername!, _protector.Unprotect(stored.ReplicationPasswordEncrypted!));
        await ProveReplicationAsync(profile, writeback, details, stored.ReplicationState, cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TargetConnectionDTO> ClearReplicationCredentialAsync(CancellationToken cancellationToken = default) {
        await _repository.ClearReplicationCredentialAsync(cancellationToken).ConfigureAwait(false);
        _provider.Invalidate();
        _changeSignal.Signal();
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private (ITargetEngineProfile Profile, IWritebackCapability Writeback) RequireWriteback(TargetConnection stored) {
        var profile = _engines.For(stored.Engine);
        return (profile, profile.Writeback
            ?? throw new ValidationException(
                $"Writeback is not supported for {stored.Engine}, so there is no Replication Credential to enter."));
    }

    /// <summary>The Target Connection's own details with the Replication Credential's login: the same database.</summary>
    private TargetConnectionDetails ReplicationDetails(TargetConnection stored, string username, string password) =>
        stored.Decrypt(_protector) with { Username = username, Password = password };

    /// <summary>Proves a Replication Credential and records the outcome, in the same terms as the connection's.</summary>
    private async Task ProveReplicationAsync(ITargetEngineProfile profile, IWritebackCapability writeback,
        TargetConnectionDetails details, ConnectionState stateBefore, CancellationToken cancellationToken) {
        string? problem;
        try {
            problem = await writeback.ProveReplicationCredentialAsync(profile.BuildConnectionString(details), cancellationToken)
                .ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            problem = $"The Replication Credential could not be proved: {ex.Message}";
        }

        var state = problem is null
            ? ConnectionState.Connected
            : stateBefore is ConnectionState.Connected or ConnectionState.Failed ? ConnectionState.Failed : ConnectionState.Incomplete;
        await _repository.RecordReplicationOutcomeAsync(state, problem, _time.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);

        if (problem is null) {
            _logger.LogInformation("Replication Credential proved");
        } else {
            _logger.LogWarning("Replication Credential not proved: {Problem}", problem);
        }

        _provider.Invalidate();
        _changeSignal.Signal();
    }

    /// <summary>
    /// Opens the database and reads its schema metadata, recording the outcome as state.
    /// </summary>
    /// <remarks>
    /// Schema metadata rather than <c>SELECT 1</c>, because that is what configuring a Binding depends on:
    /// credentials that connect but cannot see any tables must fail here, not at the first Binding.
    /// </remarks>
    private async Task ProveAsync(ITargetEngineProfile profile, TargetConnectionDetails details,
        ConnectionState stateBefore, CancellationToken cancellationToken) {
        var now = _time.GetUtcNow().UtcDateTime;

        try {
            await ReadSchemaAsync(profile, details, cancellationToken).ConfigureAwait(false);

            await _repository.RecordSuccessAsync(now, cancellationToken).ConfigureAwait(false);

            if (stateBefore is ConnectionState.Failed) {
                // Loud on purpose. The recovery is automatic so a transient outage needs no one, but the
                // outage still happened, and the operator should go and find out why.
                _logger.LogWarning(
                    "The Target Connection to {Engine} at {Address} has RECOVERED. It was Failed; events published " +
                    "while it was down were not written and will not be replayed. Investigate why the database was unreachable.",
                    details.Engine, details.Host ?? details.FilePath);
            } else {
                _logger.LogInformation("Target Connection proved against {Engine} at {Address}", details.Engine, details.Host ?? details.FilePath);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // Never proved is not the same as broken. A connection that has never worked stays Incomplete
            // ("what you typed did not work"); one that used to work becomes Failed ("something changed").
            var summary = Summarise(ex);
            if (stateBefore is ConnectionState.Connected or ConnectionState.Failed) {
                await _repository.RecordFailureAsync(summary, ex.Message, now, cancellationToken).ConfigureAwait(false);
                _logger.LogError(ex, "The Target Connection failed its proof: {Summary}", summary);
            } else {
                await _repository.RecordIncompleteAsync(summary, ex.Message, now, cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(ex, "The Target Connection could not be proved: {Summary}", summary);
            }
        }
    }

    /// <summary>
    /// The proof itself: a repository built from these details must be able to read schema metadata.
    /// </summary>
    private static Task ReadSchemaAsync(ITargetEngineProfile profile, TargetConnectionDetails details,
        CancellationToken cancellationToken) =>
        profile.CreateRepository(profile.BuildConnectionString(details))
            .GetSchemaMetadata(cancellationToken: cancellationToken);

    /// <summary>
    /// A one-line summary for the read model. The driver's own message is stored separately and shown
    /// alongside it, so this does not have to be complete — only useful.
    /// </summary>
    private static string Summarise(Exception ex) => ex switch {
        NotImplementedException => "This engine is not supported yet.",
        _ => "The Target Database could not be reached, or its schema could not be read, with these details."
    };

    /// <param name="existing">
    /// The stored connection, whose password a blank one keeps when the identity is unchanged. Null when a
    /// blank password must stay blank (nothing stored, or a repoint, which always names a new database).
    /// </param>
    /// <returns>The details, and the stored ciphertext when it was kept rather than replaced.</returns>
    private (ITargetEngineProfile Profile, TargetConnectionDetails Details, string? KeptPassword) Validate(
        SaveTargetConnectionDTO request, TargetConnection? existing) {
        if (!Enum.TryParse<TargetDatabaseEngine>(request.Engine, ignoreCase: true, out var engine)) {
            throw new ValidationException(
                $"'{request.Engine}' is not a supported engine. Choose one of: {string.Join(", ", Enum.GetNames<TargetDatabaseEngine>())}.");
        }

        var profile = _engines.For(engine);
        if (!profile.IsAvailable) {
            throw new ValidationException(
                $"{engine} cannot be used as a Target Database: {profile.UnavailableReason}");
        }

        if (!_protector.IsAvailable) {
            // Refused rather than stored in the clear. An application that protects nothing while claiming
            // to is worse than one that says it cannot yet.
            throw new ValidationException(
                "This application cannot store a database password because no protecting key is available. " +
                "Supply a protecting certificate through DataProtection:ProtectingCertificate and restart. " +
                $"Looked at: {_protector.ProtectingKeyDescription}.");
        }

        var details = new TargetConnectionDetails {
            Engine = engine,
            Host = request.Host?.Trim(),
            Port = request.Port,
            DatabaseName = request.DatabaseName?.Trim(),
            Username = request.Username?.Trim(),
            Password = string.IsNullOrEmpty(request.Password) ? null : request.Password,
            FilePath = request.FilePath?.Trim(),
            Options = request.Options
        };

        // Kept only for the database it was typed for: a blank password naming a different database reaches
        // the profile as missing, and is refused there like any other required field left empty.
        string? keptPassword = null;
        if (string.IsNullOrEmpty(details.Password) && existing?.PasswordEncrypted is { } cipher
            && existing.Identity == new TargetConnectionIdentity(details.Engine, details.Host, details.DatabaseName, details.FilePath)) {
            keptPassword = cipher;
            details = details with { Password = _protector.Unprotect(cipher) };
        }

        return (profile, details, keptPassword);
    }

    /// <summary>The profile's own checks against its field definitions: required fields, kinds, choices, options.</summary>
    private static void ThrowIfInvalid(ITargetEngineProfile profile, TargetConnectionDetails details) {
        var errors = profile.Validate(details);
        if (errors.Count > 0) {
            throw new ValidationException(string.Join(" ", errors));
        }
    }

    private string? Protect(string? password) => password is null ? null : _protector.Protect(password);

    private SecretProtectionDTO DescribeSecretProtection(TargetConnection? connection) {
        if (_protector.IsAvailable) {
            // A stored password that will not decrypt is the case that must never read as "not configured".
            if (connection?.PasswordEncrypted is { } cipher && !_protector.TryUnprotect(cipher, out _)) {
                return new SecretProtectionDTO {
                    Status = "Unreadable",
                    ProtectingKey = _protector.ProtectingKeyDescription,
                    Guidance = "A database password is stored but cannot be decrypted with the protecting key " +
                               "that is present. Restore the original protecting certificate and key ring, " +
                               "or save the connection again with the password re-entered."
                };
            }

            return new SecretProtectionDTO { Status = "Ready", ProtectingKey = _protector.ProtectingKeyDescription };
        }

        return new SecretProtectionDTO {
            Status = connection?.PasswordEncrypted is null ? "NotConfigured" : "Unreadable",
            ProtectingKey = _protector.ProtectingKeyDescription,
            Guidance = connection?.PasswordEncrypted is null
                ? "No protecting certificate is configured, so no database password can be stored yet. Supply " +
                  "one through DataProtection:ProtectingCertificate and restart."
                : "A database password is stored but no protecting key is available to read it. Restore the " +
                  "protecting certificate."
        };
    }
}
