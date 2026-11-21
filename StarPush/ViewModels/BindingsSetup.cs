using Application.Pipeline;
using Application.Targets;
using System.ComponentModel.DataAnnotations;

namespace StarPush.ViewModels;

/// <summary>
/// What the Bindings pages need before they can do anything: a Primary Channel to read Entities from, and a
/// Target Connection whose tables can be read.
/// </summary>
/// <remarks>
/// Worked out from the Pipeline status the shell is built from, so the page and the sidebar agree. A Target
/// Connection that is set up but Failed is not missing: the list still works without it, and the editor says
/// the Target Database is unreachable in place of its mappings.
/// </remarks>
public static class BindingsSetup {
    public static IReadOnlyList<SetupStepView> Missing(PipelineStatus status) {
        var steps = new List<SetupStepView>();

        if (status.PrimaryChannel.ChannelFullName is null) {
            if (status.PrimaryChannel.WaitingOn.Contains(PipelineStage.OrgConnection)) {
                steps.Add(new SetupStepView(
                    StagePages.Title(PipelineStage.OrgConnection),
                    "Channels are read from Salesforce, so the Org Connection has to work before a Primary Channel can be chosen.",
                    "Set up Org Connection",
                    StagePages.Href(PipelineStage.OrgConnection)));
            }
            steps.Add(new SetupStepView(
                StagePages.Title(PipelineStage.PrimaryChannel),
                "No Primary Channel is chosen, so there are no Entities to bind.",
                "Choose Primary Channel",
                StagePages.Href(PipelineStage.PrimaryChannel)));
        }

        if (status.TargetConnection.Status is StageStatus.ToDo or StageStatus.Waiting) {
            steps.Add(new SetupStepView(
                StagePages.Title(PipelineStage.TargetConnection),
                status.TargetConnection.Reasons.Contains(StageReason.TargetConnectionNotProved)
                    ? "The Target Connection has never been proved, so its tables cannot be read."
                    : "There is no Target Connection yet, so there are no Target Tables to bind to.",
                "Set up Target Connection",
                StagePages.Href(PipelineStage.TargetConnection)));
        }

        return steps;
    }

    /// <summary>
    /// What to show when the Target Database could not be read for a page. A refusal of this application's own
    /// (a dropped table, no Avro Schema) is its message alone; a driver failure keeps the driver's words.
    /// </summary>
    public static TargetDatabaseErrorView LoadError(Exception ex) => ex switch {
        ValidationException or KeyNotFoundException or NoTargetDatabaseException => new(ex.Message, "", null),
        _ => new("The Target Database could not be read, so its tables and columns cannot be shown.", ex.Message, null)
    };
}
