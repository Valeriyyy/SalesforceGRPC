using System.Data.Common;

namespace Application.Targets;

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
