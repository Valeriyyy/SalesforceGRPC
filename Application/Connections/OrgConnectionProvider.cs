using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Salesforce.Auth;

namespace Application.Connections;

/// <summary>
/// The one place the running application reads the Org Connection from.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> <c>IMemoryCache</c>. The schema and mapping lookups elsewhere in this codebase
/// use a one-hour sliding expiration with no invalidation hook, which is a documented gotcha: configuration
/// edited in the database does not reach the running worker until the entry expires or the process restarts.
/// Credentials must not inherit that — a user who fixes a broken connection expects it to start working, not
/// to start working within the hour. This holds the row until something calls <see cref="Invalidate"/>.
/// </remarks>
public interface IOrgConnectionProvider {
    /// <summary>The Org Connection, or null on a fresh install. Secrets are not decrypted.</summary>
    Task<OrgConnection?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The Org Connection with its Signing Keypair decrypted, or null when there is no connection.
    /// </summary>
    /// <exception cref="SecretsUnreadableException">
    /// A connection exists but its private key will not decrypt. Distinct from null, which means there is
    /// nothing to read.
    /// </exception>
    Task<SalesforceCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the held connection so the next read comes from the database.</summary>
    void Invalidate();
}

/// <inheritdoc />
public sealed class OrgConnectionProvider : IOrgConnectionProvider {
    private readonly IOrgConnectionRepository _repository;
    private readonly ISecretProtector _protector;
    private readonly ILogger<OrgConnectionProvider> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private OrgConnection? _connection;
    private bool _loaded;

    public OrgConnectionProvider(IOrgConnectionRepository repository, ISecretProtector protector,
        ILogger<OrgConnectionProvider> logger) {
        _repository = repository;
        _protector = protector;
        _logger = logger;
    }

    public async Task<OrgConnection?> GetAsync(CancellationToken cancellationToken = default) {
        if (_loaded) {
            return _connection;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_loaded) {
                return _connection;
            }

            _connection = await _repository.GetAsync(cancellationToken).ConfigureAwait(false);
            _loaded = true;
            return _connection;
        } finally {
            _loadLock.Release();
        }
    }

    public async Task<SalesforceCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default) {
        var connection = await GetAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null) {
            return null;
        }

        // Decrypted per call rather than held alongside the cached row, so the private key is not sitting in
        // the heap for the life of the process. Callers are token requests, which are rare by design.
        var privateKeyPem = _protector.Unprotect(connection.SigningPrivateKey);

        return new SalesforceCredentials {
            ConsumerKey = connection.ConsumerKey,
            AdministeringUsername = connection.AdministeringUsername,
            RunAsUsername = connection.RunAsUsername,
            SigningPrivateKeyPem = privateKeyPem,
            LoginUrl = connection.LoginUrl,
            OrgUrl = connection.OrgUrl,
            OrgId = connection.OrgId,
            CertificateFingerprint = connection.CertificateFingerprint
        };
    }

    public void Invalidate() {
        _loaded = false;
        _connection = null;
        _logger.LogDebug("Org Connection invalidated; the next read will come from the database");
    }
}
