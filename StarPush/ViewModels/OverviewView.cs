using Application.Pipeline;
using Application.Runtime;

namespace StarPush.ViewModels;

/// <summary>The Overview page: the Pipeline as four Stage cards, and the worker card.</summary>
/// <param name="NotSyncing">
/// Every Stage is OK, yet a direction of syncing is Blocked or Retrying: the header must not celebrate. The Stages
/// themselves never change for it — they say whether the Pipeline is set up, not whether it is moving.
/// </param>
public sealed record OverviewView(
    IReadOnlyList<StageView> Stages,
    PipelineStage? NextStep,
    bool AllOk,
    bool NotSyncing,
    WorkerStatusView WorkerStatus) {

    public static OverviewView For(PipelineStatus pipeline, WorkerStatus worker, DateTimeOffset now) {
        var workerStatus = WorkerStatusView.For(worker, now);
        return new OverviewView(
            pipeline.Stages.Select(s => StageView.For(s, now)).ToList(),
            pipeline.NextStep,
            pipeline.AllOk,
            pipeline.AllOk && workerStatus.IsNotSyncing(),
            workerStatus);
    }
}
