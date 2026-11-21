using Database.Models;

namespace Application.Pipeline;

public sealed record PrimaryChannelStage : StageState {
    public override PipelineStage Stage => PipelineStage.PrimaryChannel;
    public string? ChannelFullName { get; init; }
    public int MemberCount { get; init; }
    public DateTime? CheckpointSavedAt { get; init; }
    public TimeSpan? CheckpointAge { get; init; }

    /// <summary>How long until Salesforce discards the events after the Checkpoint; zero once it has.</summary>
    public TimeSpan? CheckpointExpiresIn { get; init; }

    /// <summary>Where the worker begins while there is no Checkpoint.</summary>
    public StartingPoint? StartingPoint { get; init; }
}
