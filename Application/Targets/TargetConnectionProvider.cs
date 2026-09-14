using Application.Connections;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace Application.Targets;

/// <summary>
/// Thrown when something needs the Target Database and no Target Connection is configured.
/// </summary>
public sealed class NoTargetDatabaseException : InvalidOperationException {
    public NoTargetDatabaseException()
        : base("No Target Connection has been set up, so there is no database to write to. " +
               "Configure one through the API.") { }
}

/// <summary>
/// The one place the running application reads the Target Connection from, and the one place a repository
/// for it is built.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> <c>IMemoryCache</c>, for the reason <see cref="IOrgConnectionProvider"/> gives:
/// a user who fixes a broken connection expects it to start working, not to start working within the hour.
/// This holds what it has built until something calls <see cref="Invalidate"/>.
/// </remarks>
public interface ITargetConnectionProvider {
    /// <summary>The Target Connection, or null on a fresh install. The password is not decrypted.</summary>
    Task<TargetConnection?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The repository for the current Target Connection.
    /// </summary>
    /// <exception cref="NoTargetDatabaseException">Nothing is configured.</exception>
    /// <exception cref="SecretsUnreadableException">The stored password will not decrypt.</exception>
    Task<IRepository> GetRepositoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops what is held so the next read comes from the database.</summary>
    void Invalidate();
}

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
            loaded.Repository = profile.CreateRepository(connectionString);
            return loaded.Repository;
        } finally {
            _loadLock.Release();
        }
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

/// <summary>
/// The Target Database could not be reached, or refused the connection, during a write.
/// </summary>
/// <remarks>
/// Distinct from every other failure inside the worker's batch so it can escape the per-event isolation. A
/// bad record — a constraint violation, a type mismatch, a missing column — is that record's problem: it is
/// logged and skipped and the batch continues, as it always has. A database that cannot be reached is
/// everyone's problem: it ends the stream, because consuming events into a database that cannot store them
/// loses them with no record of how many. <see cref="IsDatabaseUnavailable"/> draws that line.
/// </remarks>
public sealed class TargetDatabaseWriteException : Exception {
    public TargetDatabaseWriteException(string table, DbException inner)
        : base($"The Target Database could not be reached while writing to {table}: {inner.Message}", inner) { }

    /// <summary>
    /// Whether a driver failure is about the database rather than the data.
    /// </summary>
    /// <remarks>
    /// Engine-agnostic on purpose, using only what <see cref="DbException"/> itself exposes. A null SQLSTATE
    /// is a client-side failure (no route, refused, timed out) that never reached a server. Class 08 is the
    /// SQL standard's "connection exception" and class 28 its "invalid authorization", and every supported
    /// engine reports those the same way. Everything else — 22 data, 23 integrity, 42 syntax or access — is
    /// one record's fault and stays inside the batch.
    /// </remarks>
    public static bool IsDatabaseUnavailable(DbException ex) =>
        ex.IsTransient
        || ex.SqlState is null
        || ex.SqlState.StartsWith("08", StringComparison.Ordinal)
        || ex.SqlState.StartsWith("28", StringComparison.Ordinal);
}

/// <summary>
/// The one place a stored Target Connection becomes plaintext details.
/// </summary>
public static class TargetConnectionDecryption {
    /// <exception cref="SecretsUnreadableException">The stored password will not decrypt.</exception>
    public static TargetConnectionDetails Decrypt(this TargetConnection connection, ISecretProtector protector) => new() {
        Engine = connection.Engine,
        Host = connection.Host,
        Port = connection.Port,
        DatabaseName = connection.DatabaseName,
        Username = connection.Username,
        Password = connection.PasswordEncrypted is { } cipher ? protector.Unprotect(cipher) : null,
        FilePath = connection.FilePath,
        Options = connection.Options
    };
}
