using Database.Models;
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
    Task<OrgConnectionDetails?> GetDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the held connection so the next read comes from the database.</summary>
    void Invalidate();
}
