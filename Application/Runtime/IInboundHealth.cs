namespace Application.Runtime;

/// <summary>
/// What the inbound worker is doing right now, and what it has done since the application started. Kept in
/// memory: it describes this process, and the API that reads it runs in the same one.
/// </summary>
public interface IInboundHealth {
    RunState State { get; }

    /// <summary>Why it is Blocked or Retrying; null otherwise.</summary>
    string? Reason { get; }

    /// <summary>When a Retrying worker tries again, when it knows.</summary>
    DateTimeOffset? NextAttemptAt { get; }

    /// <summary>When the current state was reported: for Running, when the stream was opened.</summary>
    DateTimeOffset StateSince { get; }

    /// <summary>True from startup until the worker first reports a state.</summary>
    bool Starting { get; }

    DateTimeOffset StartedAt { get; }

    /// <summary>When Salesforce last sent anything on the stream, keepalives included.</summary>
    DateTimeOffset? LastHeardAt { get; }

    long EventsApplied { get; }

    /// <summary>Inbound Failures recorded since startup.</summary>
    long FailuresRecorded { get; }

    DateTimeOffset? LastAppliedAt { get; }
}
