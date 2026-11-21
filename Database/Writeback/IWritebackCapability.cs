using Database.Repositories.Interfaces;

namespace Database.Writeback;

/// <summary>
/// Everything about Writeback that differs per Target Database Engine: reading the database's changes, setting
/// the database up for that, and tagging this application's own writes so they are never read back.
/// </summary>
/// <remarks>
/// Offered by an engine profile only when the engine supports Writeback. Everything after the change stream —
/// Field Directions, mapping back to Salesforce, the Salesforce calls, failures — is shared and engine-neutral
/// (ADR 0007).
/// </remarks>
public interface IWritebackCapability {
    /// <summary>
    /// Why this particular database cannot do Two-way at all (a server too old), or null when it can. Unlike a
    /// prerequisite, nothing the user can grant or configure changes this.
    /// </summary>
    Task<string?> GetUnavailableReasonAsync(string connectionString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every unmet prerequisite for Writeback of these tables, each with the SQL that fixes it. Empty when the
    /// database is ready.
    /// </summary>
    Task<IReadOnlyList<WritebackPrerequisite>> CheckPrerequisitesAsync(WritebackEndpoints endpoints,
        IReadOnlyList<WritebackTable> tables, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates whatever this application's writes are tagged with, so a tagged repository can be handed out before
    /// <see cref="ArmAsync"/> starts the database keeping changes. Safe to repeat.
    /// </summary>
    Task PrepareTagAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the database up so exactly these tables are read for Writeback. Safe to repeat; a table no longer
    /// listed stops being read.
    /// </summary>
    Task ArmAsync(WritebackEndpoints endpoints, IReadOnlyList<WritebackTable> tables,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes everything <see cref="ArmAsync"/> set up away again, so the database no longer keeps changes for
    /// a reader. Safe to repeat.
    /// </summary>
    Task DisarmAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default);

    /// <summary>Opens the change stream, resuming after the last confirmed position.</summary>
    Task<IWritebackChangeStream> OpenStreamAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// A repository whose writes carry this application's tag, so the change stream leaves them out. Every
    /// write this application makes to an armed database goes through one.
    /// </summary>
    IRepository CreateTaggedRepository(string connectionString);

    /// <summary>How far the reader is behind the database, in bytes; null when nothing is armed.</summary>
    Task<long?> GetLagBytesAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proves a Replication Credential: it logs in, may read changes, and the server can do Two-way. Returns
    /// null on success, otherwise what is wrong in words the user can act on.
    /// </summary>
    Task<string?> ProveReplicationCredentialAsync(string replicationConnectionString,
        CancellationToken cancellationToken = default);
}
