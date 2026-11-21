using Application.Pipeline;

namespace StarPush.ViewModels;

/// <summary>Where each Stage is set up, and its place in the setup order.</summary>
public static class StagePages {
    public const string OverviewHref = "/";

    public static string Href(PipelineStage stage) => stage switch {
        PipelineStage.OrgConnection => "/org-connection",
        PipelineStage.PrimaryChannel => "/channels",
        PipelineStage.TargetConnection => "/target-connection",
        PipelineStage.Bindings => "/bindings",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };

    public static int StepNumber(PipelineStage stage) => (int)stage + 1;

    public static string Title(PipelineStage stage) => stage switch {
        PipelineStage.OrgConnection => "Org Connection",
        PipelineStage.PrimaryChannel => "Primary Channel",
        PipelineStage.TargetConnection => "Target Connection",
        PipelineStage.Bindings => "Bindings",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };
}
