using Application.Runtime;

namespace Application.Writeback;

/// <summary>
/// What the Writeback worker is doing right now, for the pipeline status and telemetry. Kept in memory: it
/// describes this process, not stored configuration.
/// </summary>
public interface IWritebackHealth {
    RunState State { get; }
    string? Reason { get; }

    /// <summary>How far the reader is behind the Target Database, in bytes, when last measured.</summary>
    long? LagBytes { get; }

    DateTimeOffset? LastAppliedAt { get; }
}
