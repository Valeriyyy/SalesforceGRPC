namespace Application.Runtime;

/// <summary>The one <see cref="IInboundHealth"/>, written by the inbound worker.</summary>
public sealed class InboundHealth : IInboundHealth {
    /// <summary>
    /// Running with nothing heard from Salesforce for longer than this is suspicious: an idle stream still gets
    /// a keepalive every few minutes.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public const string StartingReason = "Starting…";

    private sealed record Snapshot(RunState State, string? Reason, DateTimeOffset? NextAttemptAt, bool Starting,
        DateTimeOffset Since);

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private volatile Snapshot _current;
    private long _eventsApplied;
    private long _failuresRecorded;
    private DateTimeOffset? _lastHeardAt;
    private DateTimeOffset? _lastAppliedAt;

    public InboundHealth(TimeProvider time) {
        _time = time;
        StartedAt = time.GetUtcNow();
        _current = new Snapshot(RunState.Retrying, StartingReason, null, true, StartedAt);
    }

    public RunState State => _current.State;
    public string? Reason => _current.Reason;
    public DateTimeOffset? NextAttemptAt => _current.NextAttemptAt;
    public bool Starting => _current.Starting;
    public DateTimeOffset StateSince => _current.Since;
    public DateTimeOffset StartedAt { get; }
    public long EventsApplied => Interlocked.Read(ref _eventsApplied);
    public long FailuresRecorded => Interlocked.Read(ref _failuresRecorded);

    public DateTimeOffset? LastHeardAt {
        get { lock (_gate) { return _lastHeardAt; } }
    }

    public DateTimeOffset? LastAppliedAt {
        get { lock (_gate) { return _lastAppliedAt; } }
    }

    public void Set(RunState state, string? reason = null, DateTimeOffset? nextAttemptAt = null) =>
        _current = new Snapshot(state, reason, nextAttemptAt, false, _time.GetUtcNow());

    public void Heard(DateTimeOffset at) {
        lock (_gate) { _lastHeardAt = at; }
    }

    /// <summary>Counts one event applied. Called from parallel ordering groups.</summary>
    public void Applied(DateTimeOffset at) {
        Interlocked.Increment(ref _eventsApplied);
        lock (_gate) {
            if (_lastAppliedAt is null || at > _lastAppliedAt) {
                _lastAppliedAt = at;
            }
        }
    }

    public void Failed() => Interlocked.Increment(ref _failuresRecorded);
}
