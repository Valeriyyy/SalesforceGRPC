using System.Data.Common;
using Database.Targets;

namespace Application.Targets;

/// <summary>
/// The Target Database is up but refused one change's write: a value too long, a missing required value, a
/// duplicate. That change's fault, not an outage, so it is recorded as an Inbound Failure and the batch carries on.
/// </summary>
/// <remarks>
/// Carries what the user needs to understand the refusal and fix its cause: the table, the values being written,
/// and the SQLSTATE and column or constraint the database named.
/// </remarks>
public sealed class TargetRowRefusedException : Exception {
    public TargetRowRefusedException(string table, IReadOnlyDictionary<string, object?> values, DbException inner)
        : base(inner.Message, inner) {
        Table = table;
        Values = values;
        SqlState = inner.SqlState;
        (Column, Constraint) = TargetErrorDetail.Of(inner);
    }

    public string Table { get; }

    /// <summary>The column values the write carried, keyed by Target Column.</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }

    public string? SqlState { get; }
    public string? Column { get; }
    public string? Constraint { get; }
}
