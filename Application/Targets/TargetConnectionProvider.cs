using Application.Connections;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using Microsoft.Extensions.Logging;

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
            var connectionString = profile.BuildConnectionString(Decrypt(connection));
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

    private TargetConnectionDetails Decrypt(TargetConnection connection) => new() {
        Engine = connection.Engine,
        Host = connection.Host,
        Port = connection.Port,
        DatabaseName = connection.DatabaseName,
        Username = connection.Username,
        Password = connection.PasswordEncrypted is { } cipher ? _protector.Unprotect(cipher) : null,
        FilePath = connection.FilePath,
        Options = connection.Options
    };

    public void Invalidate() {
        Volatile.Write(ref _loaded, null);
        _logger.LogDebug("Target Connection invalidated; the next read will come from the database");
    }
}
