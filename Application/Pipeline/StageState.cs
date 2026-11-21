namespace Application.Pipeline;

/// <summary>One Stage's status, what it waits on, and why.</summary>
public abstract record StageState {
    public abstract PipelineStage Stage { get; }
    public StageStatus Status { get; init; }

    /// <summary>The earlier Stages that are not OK. Non-empty only when <see cref="Status"/> is Waiting.</summary>
    public IReadOnlyList<PipelineStage> WaitingOn { get; init; } = [];

    /// <summary>Why the Stage is To do or Needs attention. Empty when it is OK or Waiting.</summary>
    public IReadOnlyList<StageReason> Reasons { get; init; } = [];
}
