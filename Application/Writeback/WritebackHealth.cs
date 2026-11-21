namespace Application.Writeback;

public enum WritebackRunState {
    /// <summary>No Binding is Two-way and Active, so there is nothing to write back.</summary>
    Idle,

    /// <summary>A Binding wants Writeback but something it needs is missing; <see cref="IWritebackHealth.Reason"/> says what.</summary>
    Blocked,

    /// <summary>Reading the Target Database's changes and writing them back.</summary>
    Running,

    /// <summary>Paused after a failure that may pass, and about to try again.</summary>
    Retrying
}

/// <summary>
/// What the Writeback worker is doing right now, for the pipeline status and telemetry. Kept in memory: it
/// describes this process, not stored configuration.
/// </summary>
public interface IWritebackHealth {
    WritebackRunState State { get; }
    string? Reason { get; }

    /// <summary>How far the reader is behind the Target Database, in bytes, when last measured.</summary>
    long? LagBytes { get; }

    DateTimeOffset? LastAppliedAt { get; }
}

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
