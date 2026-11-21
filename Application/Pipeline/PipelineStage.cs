namespace Application.Pipeline;

/// <summary>The four Stages of the Pipeline, in the order they are set up.</summary>
public enum PipelineStage {
    OrgConnection,
    PrimaryChannel,
    TargetConnection,
    Bindings
}
