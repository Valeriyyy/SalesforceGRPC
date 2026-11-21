namespace Database.Writeback;

/// <summary>
/// The changes one Target Database transaction made, in commit order, and the position to confirm once they
/// have all been dealt with.
/// </summary>
public sealed class ChangeTransaction {
    public required IReadOnlyList<RowChange> Changes { get; init; }
    public required WritebackPosition Position { get; init; }
}
