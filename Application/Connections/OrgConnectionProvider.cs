using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Salesforce.Auth;

namespace Application.Connections;

/// <inheritdoc />
public sealed class OrgConnectionProvider : IOrgConnectionProvider {
    private readonly IOrgConnectionRepository _repository;
    private readonly ISecretProtector _protector;
    private readonly ILogger<OrgConnectionProvider> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <summary>
    /// What has been loaded, held as one reference so a reader never sees half of it.
    /// </summary>
    /// <remarks>
    /// A "loaded" flag beside a separate connection field would be two writes with no ordering between them,
    /// and a reader outside the lock could see the flag set before the connection was visible — reporting no
    /// Org Connection for one that exists, on exactly the code path that decides whether the worker runs. One
    /// reference, written and read with <see cref="Volatile"/>, has no half-state to observe.
    /// </remarks>
    private sealed record Loaded(OrgConnection? Connection);

    private Loaded? _loaded;

    public OrgConnectionProvider(IOrgConnectionRepository repository, ISecretProtector protector,
        ILogger<OrgConnectionProvider> logger) {
        _repository = repository;
        _protector = protector;
        _logger = logger;
    }

    public async Task<OrgConnection?> GetAsync(CancellationToken cancellationToken = default) {
        if (Volatile.Read(ref _loaded) is { } alreadyLoaded) {
            return alreadyLoaded.Connection;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (Volatile.Read(ref _loaded) is { } loadedWhileWaiting) {
                return loadedWhileWaiting.Connection;
            }

            var connection = await _repository.GetAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _loaded, new Loaded(connection));
            return connection;
        } finally {
            _loadLock.Release();
        }
    }

    public async Task<OrgConnectionDetails?> GetDetailsAsync(CancellationToken cancellationToken = default) {
        var connection = await GetAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null) {
            return null;
        }

        // Decrypted per call rather than held alongside the cached row, so the private key is not sitting in
        // the heap for the life of the process. Callers are token requests, which are rare by design.
        var privateKeyPem = _protector.Unprotect(connection.SigningPrivateKey);

        return new OrgConnectionDetails {
            ConsumerKey = connection.ConsumerKey,
            AdministeringUsername = connection.AdministeringUsername,
            RunAsUsername = connection.RunAsUsername,
            SigningPrivateKeyPem = privateKeyPem,
            LoginHost = SalesforceLoginHost.For(connection.IsSandbox),
            OrgUrl = connection.OrgUrl,
            OrgId = connection.OrgId,
            CertificateFingerprint = connection.CertificateFingerprint
        };
    }

    public void Invalidate() {
        Volatile.Write(ref _loaded, null);
        _logger.LogDebug("Org Connection invalidated; the next read will come from the database");
    }
}
