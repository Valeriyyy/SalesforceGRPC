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

/// <summary>
/// The two logins Writeback uses: the Target Connection's own, which writes rows and sets the database up, and
/// the Replication Credential, which only reads changes.
/// </summary>
public sealed record WritebackEndpoints(string ConnectionString, string ReplicationConnectionString);

/// <summary>A Target Table, named the way its engine names it (schema null where the engine has none).</summary>
public sealed record WritebackTable(string? Schema, string Table) {
    public override string ToString() => Schema is null ? Table : $"{Schema}.{Table}";
}

/// <summary>Something the user must change before Writeback can run, and the SQL that does it.</summary>
public sealed record WritebackPrerequisite(string Message, string? FixSql = null);

public enum RowChangeKind {
    Insert,
    Update,
    Delete
}

/// <summary>
/// One row inserted, updated or deleted in a Target Table, in engine-neutral form.
/// </summary>
public sealed class RowChange {
    public required WritebackTable Table { get; init; }
    public required RowChangeKind Kind { get; init; }

    /// <summary>The row after the change, column name to value. Empty for a delete.</summary>
    public required IReadOnlyDictionary<string, object?> NewValues { get; init; }

    /// <summary>
    /// The row before an update, when the engine provides it; null otherwise, in which case every column must
    /// be assumed to have changed.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? OldValues { get; init; }

    /// <summary>
    /// Columns the engine did not resend because they did not change (Postgres: unchanged TOASTed values).
    /// Their entries in <see cref="NewValues"/> are meaningless.
    /// </summary>
    public IReadOnlySet<string> UnchangedColumns { get; init; } = new HashSet<string>();
}

/// <summary>
/// The changes one Target Database transaction made, in commit order, and the position to confirm once they
/// have all been dealt with.
/// </summary>
public sealed class ChangeTransaction {
    public required IReadOnlyList<RowChange> Changes { get; init; }
    public required WritebackPosition Position { get; init; }
}

/// <summary>An opaque position in a database's change stream.</summary>
public readonly record struct WritebackPosition(ulong Value);

/// <summary>
/// A database's changes, one committed transaction at a time, excluding this application's own writes.
/// </summary>
public interface IWritebackChangeStream : IAsyncDisposable {
    IAsyncEnumerable<ChangeTransaction> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms everything up to a position has been sent to Salesforce or recorded as a Writeback Failure, so
    /// the database may let it go and the stream never resends it.
    /// </summary>
    Task ConfirmAsync(WritebackPosition position, CancellationToken cancellationToken = default);
}
