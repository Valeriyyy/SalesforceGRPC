namespace Application.Writeback;

/// <summary>The one <see cref="IWritebackHealth"/>, written by the Writeback worker.</summary>
public sealed class WritebackHealth : IWritebackHealth {
    /// <summary>Lag beyond this shows the Bindings stage as Needs attention: the database is keeping a lot for us.</summary>
    public const long LagAttentionBytes = 1L << 30;

    private volatile Snapshot _current = new(WritebackRunState.Idle, null, null, null);

    private sealed record Snapshot(WritebackRunState State, string? Reason, long? LagBytes, DateTimeOffset? LastAppliedAt);

    public WritebackRunState State => _current.State;
    public string? Reason => _current.Reason;
    public long? LagBytes => _current.LagBytes;
    public DateTimeOffset? LastAppliedAt => _current.LastAppliedAt;

    public void Set(WritebackRunState state, string? reason = null) =>
        _current = _current with { State = state, Reason = reason, LagBytes = state is WritebackRunState.Idle ? null : _current.LagBytes };

    public void Applied(DateTimeOffset at) => _current = _current with { LastAppliedAt = at };

    public void Lag(long? bytes) => _current = _current with { LagBytes = bytes };
}
