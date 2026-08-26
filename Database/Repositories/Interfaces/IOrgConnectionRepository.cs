using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// Counts of the org-specific state Disconnect destroys, so the user can be told what they are about to lose
/// before they lose it.
/// </summary>
public sealed record OrgScopedStateCounts {
    public int Bindings { get; init; }
    public int FieldMappings { get; init; }
    public int AvroSchemas { get; init; }
    public int Channels { get; init; }
    public int ChannelMembers { get; init; }
}

/// <summary>
/// Reads and writes the single Org Connection row, and the org-specific state that only makes sense
/// alongside it.
/// </summary>
/// <remarks>
/// Nothing here caches. The connection is read through <c>IOrgConnectionProvider</c>, which invalidates
/// explicitly on save — the schema and mapping caches elsewhere in this codebase use a one-hour sliding
/// expiration with no invalidation hook, and credentials must not inherit that.
/// </remarks>
public interface IOrgConnectionRepository {
    /// <summary>The Org Connection, or null on a fresh install.</summary>
    Task<OrgConnection?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the Org Connection, or replaces the user-supplied half of the existing one.
    /// </summary>
    /// <remarks>
    /// Discovered fields and connection state are not written here — they are the outcome of a token
    /// exchange, not something a caller supplies. Editing the details resets the connection to Incomplete,
    /// because the previous success said nothing about the new details.
    /// </remarks>
    Task<OrgConnection> UpsertAsync(OrgConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Records a successful token exchange, moving the connection to Connected.</summary>
    Task RecordSuccessAsync(string orgUrl, string orgId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed token exchange, moving the connection to Failed and keeping the raw Salesforce text.
    /// </summary>
    Task RecordFailureAsync(string error, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Stores the encrypted Bootstrap session material.</summary>
    Task SaveBootstrapSecretsAsync(string? encryptedConsumerSecret, string? encryptedRefreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the Bootstrap session material once it is no longer needed.
    /// </summary>
    /// <remarks>
    /// Called after the first JWT token succeeds, not after the metadata deploy returns. Salesforce deploys
    /// are eventually consistent, so "deploy succeeded, JWT not yet working" is an expected transient state
    /// and the borrowed session is the only way to retry it without sending the user back through Setup.
    /// </remarks>
    Task PurgeBootstrapSecretsAsync(CancellationToken cancellationToken = default);

    /// <summary>How much org-specific state Disconnect would destroy.</summary>
    Task<OrgScopedStateCounts> CountOrgScopedStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the Org Connection and every piece of state that only means anything in the context of that
    /// org, in one transaction.
    /// </summary>
    /// <remarks>
    /// One transaction because a half-wipe leaves the application describing Bindings for an org it is no
    /// longer connected to — a state neither the user nor the API can explain. Nothing inside Salesforce is
    /// touched; see the Disconnect issue for why an orphaned External Client App beats a half-deleted one.
    /// </remarks>
    Task<OrgScopedStateCounts> DeleteConnectionAndOrgScopedStateAsync(CancellationToken cancellationToken = default);
}
