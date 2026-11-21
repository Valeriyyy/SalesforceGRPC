namespace Database.Writeback;

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
