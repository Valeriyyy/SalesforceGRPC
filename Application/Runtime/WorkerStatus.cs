using Database.Models;

namespace Application.Runtime;

/// <summary>What both directions of syncing are doing now, and what has happened lately, as domain data.</summary>
public sealed record WorkerStatus {
    public required InboundRun Inbound { get; init; }

    /// <summary>Null when Writeback is Idle: no Binding is Two-way, so there is nothing to say about it.</summary>
    public WritebackRun? Writeback { get; init; }

    /// <summary>The Incidents of the last 24 hours, latest first.</summary>
    public IReadOnlyList<Incident> RecentIncidents { get; init; } = [];

    /// <summary>Inbound Failures waiting for the user, however old.</summary>
    public int InboundFailures { get; init; }

    /// <summary>Writeback Failures waiting for the user.</summary>
    public int WritebackFailures { get; init; }
}

/// <summary>The inbound worker's Run State, and its counts since the application started.</summary>
public sealed record InboundRun {
    public RunState State { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public bool Starting { get; init; }

    /// <summary>When the current state was reported: for Running, when the stream was opened.</summary>
    public DateTimeOffset StateSince { get; init; }

    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? LastHeardAt { get; init; }
    public long EventsApplied { get; init; }
    public DateTimeOffset? LastAppliedAt { get; init; }

    public static InboundRun From(IInboundHealth health) => new() {
        State = health.State,
        Reason = health.Reason,
        NextAttemptAt = health.NextAttemptAt,
        Starting = health.Starting,
        StateSince = health.StateSince,
        StartedAt = health.StartedAt,
        LastHeardAt = health.LastHeardAt,
        EventsApplied = health.EventsApplied,
        LastAppliedAt = health.LastAppliedAt
    };
}

/// <summary>The Writeback worker's Run State.</summary>
public sealed record WritebackRun {
    public RunState State { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? LastAppliedAt { get; init; }
    public long? LagBytes { get; init; }
}
