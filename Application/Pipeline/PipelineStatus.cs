namespace Application.Pipeline;

/// <summary>Every Stage of the Pipeline, and which one to do next.</summary>
public sealed record PipelineStatus(
    OrgConnectionStage OrgConnection,
    PrimaryChannelStage PrimaryChannel,
    TargetConnectionStage TargetConnection,
    BindingsStage Bindings) {

    /// <summary>The four Stages in setup order.</summary>
    public IReadOnlyList<StageState> Stages => [OrgConnection, PrimaryChannel, TargetConnection, Bindings];

    /// <summary>The earliest Stage that is not OK, or null when every one is.</summary>
    public PipelineStage? NextStep => Stages.FirstOrDefault(s => s.Status != StageStatus.Ok)?.Stage;

    public bool AllOk => NextStep is null;
}
