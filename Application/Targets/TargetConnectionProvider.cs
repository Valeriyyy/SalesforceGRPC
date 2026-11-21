using Application.Connections;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using Database.Writeback;
using Microsoft.Extensions.Logging;

namespace Application.Targets;

/// <inheritdoc />
public sealed class TargetConnectionProvider : ITargetConnectionProvider {
    private readonly ITargetConnectionRepository _repository;
    private readonly ITargetEngineCatalog _engines;
    private readonly ISecretProtector _protector;
    private readonly ILogger<TargetConnectionProvider> _logger;

    public TargetConnectionProvider(ITargetConnectionRepository repository, ITargetEngineCatalog engines,
        ISecretProtector protector, ILogger<TargetConnectionProvider> logger) {
        _repository = repository;
        _engines = engines;
        _protector = protector;
        _logger = logger;
    }

    /// <summary>
    /// What has been loaded, held as one reference so a reader never sees half of it.
    /// </summary>
    /// <remarks>
    /// Same reasoning as <see cref="OrgConnectionProvider"/>: a "loaded" flag beside separate fields is two
    /// writes with no ordering between them. The repository is built lazily and held here too, so the
    /// decrypted password exists only for the duration of one <c>BuildConnectionString</c> call.
    /// </remarks>
    private sealed class Loaded {
        public required TargetConnection? Connection { get; init; }
        public IRepository? Repository { get; set; }
    }

    private Loaded? _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public async Task<TargetConnection?> GetAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Connection;

    public async Task<IRepository> GetRepositoryAsync(CancellationToken cancellationToken = default) {
        var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var connection = loaded.Connection ?? throw new NoTargetDatabaseException();

        if (loaded.Repository is { } already) {
            return already;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (loaded.Repository is { } builtWhileWaiting) {
                return builtWhileWaiting;
            }

            var profile = _engines.For(connection.Engine);
            var connectionString = profile.BuildConnectionString(connection.Decrypt(_protector));
            // While Writeback is armed, every write this application makes carries its tag, so the change
            // stream never reads the inbound flow's writes back as changes to send to Salesforce (ADR 0007).
            loaded.Repository = connection.WritebackArmedAt is not null && profile.Writeback is { } writeback
                ? writeback.CreateTaggedRepository(connectionString)
                : profile.CreateRepository(connectionString);
            return loaded.Repository;
        } finally {
            _loadLock.Release();
        }
    }

    public async Task DisarmWritebackAsync(CancellationToken cancellationToken = default) {
        var connection = (await LoadAsync(cancellationToken).ConfigureAwait(false)).Connection;
        if (connection?.WritebackArmedAt is null || _engines.For(connection.Engine).Writeback is not { } writeback) {
            return;
        }

        try {
            var endpoints = await GetWritebackEndpointsAsync(cancellationToken).ConfigureAwait(false);
            if (endpoints is null) {
                _logger.LogWarning(
                    "Writeback is armed in {Engine} at {Address} but there is no Replication Credential to take it down with; the database may keep changes until someone drops the slot {Slot}",
                    connection.Engine, connection.Host ?? connection.FilePath, "starpush_writeback");
                return;
            }
            await writeback.DisarmAsync(endpoints, cancellationToken).ConfigureAwait(false);
            await _repository.SetWritebackArmedAsync(null, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogWarning(ex,
                "Could not take Writeback down in {Engine} at {Address}; the database may keep changes until its replication slot is dropped",
                connection.Engine, connection.Host ?? connection.FilePath);
        } finally {
            Invalidate();
        }
    }

    public async Task<WritebackEndpoints?> GetWritebackEndpointsAsync(CancellationToken cancellationToken = default) {
        var connection = (await LoadAsync(cancellationToken).ConfigureAwait(false)).Connection;
        if (connection is null || !connection.HasReplicationCredential) {
            return null;
        }

        var profile = _engines.For(connection.Engine);
        var details = connection.Decrypt(_protector);
        var replication = details with {
            Username = connection.ReplicationUsername,
            Password = _protector.Unprotect(connection.ReplicationPasswordEncrypted!)
        };
        return new WritebackEndpoints(profile.BuildConnectionString(details), profile.BuildConnectionString(replication));
    }

    private async Task<Loaded> LoadAsync(CancellationToken cancellationToken) {
        if (Volatile.Read(ref _loaded) is { } alreadyLoaded) {
            return alreadyLoaded;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (Volatile.Read(ref _loaded) is { } loadedWhileWaiting) {
                return loadedWhileWaiting;
            }

            var loaded = new Loaded { Connection = await _repository.GetAsync(cancellationToken).ConfigureAwait(false) };
            Volatile.Write(ref _loaded, loaded);
            return loaded;
        } finally {
            _loadLock.Release();
        }
    }

    public void Invalidate() {
        Volatile.Write(ref _loaded, null);
        _logger.LogDebug("Target Connection invalidated; the next read will come from the database");
    }
}
