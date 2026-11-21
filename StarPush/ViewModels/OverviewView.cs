using Application.Pipeline;

namespace StarPush.ViewModels;

/// <summary>The Overview page: the Pipeline as four Stage cards, and the reserved worker-status card.</summary>
public sealed record OverviewView(
    IReadOnlyList<StageView> Stages,
    PipelineStage? NextStep,
    bool AllOk,
    WorkerStatusView? WorkerStatus) {

    public static OverviewView For(PipelineStatus pipeline, DateTimeOffset now) => new(
        pipeline.Stages.Select(s => StageView.For(s, now)).ToList(),
        pipeline.NextStep,
        pipeline.AllOk,
        WorkerStatus: null);
}
