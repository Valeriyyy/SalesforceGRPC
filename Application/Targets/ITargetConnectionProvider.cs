using Application.Connections;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Writeback;

namespace Application.Targets;

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

    /// <summary>
    /// The two logins Writeback uses — the Target Connection's own and the Replication Credential — or null
    /// when there is no Target Connection or no Replication Credential.
    /// </summary>
    Task<WritebackEndpoints?> GetWritebackEndpointsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes Writeback down in the current Target Database, so it stops keeping changes for a reader that is
    /// about to be gone. Best effort: a database that cannot be reached is logged, never thrown, because the
    /// Repoint or Disconnect calling this must still go ahead.
    /// </summary>
    Task DisarmWritebackAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops what is held so the next read comes from the database.</summary>
    void Invalidate();
}
