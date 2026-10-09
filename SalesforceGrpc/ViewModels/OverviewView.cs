using Application.Pipeline;
using Database.Models;

namespace SalesforceGrpc.ViewModels;

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

/// <summary>
/// Reserved for the worker's runtime status. Always null until the worker reports it; the card says so.
/// </summary>
public sealed record WorkerStatusView;

/// <summary>One Stage card: where it stands and, when it is not OK, what is wrong and where to fix it.</summary>
public sealed record StageView(
    PipelineStage Stage,
    int StepNumber,
    string Title,
    StageStatus Status,
    string Summary,
    string? Problem,
    string? WhatToDo,
    string? ActionLabel,
    string? ActionHref,
    IReadOnlyList<string> WaitingOn) {

    public static StageView For(StageState stage, DateTimeOffset now) {
        var waitingOn = stage.WaitingOn.Select(StagePages.Title).ToList();
        var wording = stage.Reasons.Select(r => Explain(r, stage)).ToList();
        var hasProblem = stage.Status is StageStatus.ToDo or StageStatus.NeedsAttention && wording.Count > 0;

        return new StageView(
            stage.Stage,
            StagePages.StepNumber(stage.Stage),
            StagePages.Title(stage.Stage),
            stage.Status,
            stage.Status == StageStatus.Waiting ? $"Waiting on {string.Join(" and ", waitingOn)}" : Summarise(stage, now),
            hasProblem ? string.Join(" ", wording.Select(w => w.Problem)) : null,
            hasProblem ? string.Join(" ", wording.Select(w => w.WhatToDo).Distinct()) : null,
            hasProblem ? ActionLabelFor(stage) : null,
            hasProblem ? StagePages.Href(stage.Stage) : null,
            waitingOn);
    }

    #region Summary

    private static string Summarise(StageState stage, DateTimeOffset now) => stage switch {
        OrgConnectionStage org => org.Reasons switch {
            [StageReason.NoOrgConnection] => "Not connected yet",
            [StageReason.OrgConnectionIncomplete] => $"Set up for {org.RunAsUsername}, not connected yet",
            [StageReason.OrgConnectionFailed] => org.LastConnectedAt is { } at
                ? $"Failed · last connected {Ago(now, at)}"
                : "Failed",
            _ => $"Connected as {org.RunAsUsername} to {Host(org.OrgUrl)}"
        },
        PrimaryChannelStage channel when channel.ChannelFullName is null => "No Primary Channel chosen",
        PrimaryChannelStage channel => Join(
            channel.ChannelFullName,
            Plural(channel.MemberCount, "Channel Member"),
            channel is { CheckpointAge: { } age, CheckpointExpiresIn: { } left }
                ? $"Checkpoint {Duration(age)} ago, expires in {Duration(left)}"
                : $"no Checkpoint yet, starts at {channel.StartingPoint}"),
        TargetConnectionStage target when target.Engine is null => "Not set up yet",
        TargetConnectionStage target => Join(
            target.Engine.ToString(),
            Address(target),
            target.LastConnectedAt is { } proved ? $"proved {Ago(now, proved)}" : "not proved yet"),
        BindingsStage { Counts: var c } when c.Active + c.Incomplete + c.Inactive == 0 => "No Bindings yet",
        BindingsStage { Counts: var c } => $"{c.Active} Active · {c.Incomplete} Incomplete · {c.Inactive} Inactive",
        _ => ""
    };

    private static string Address(TargetConnectionStage target) {
        if (target.Engine == TargetDatabaseEngine.Sqlite) {
            return target.FilePath ?? "";
        }

        var host = target.Port is { } port ? $"{target.Host}:{port}" : target.Host;
        return target.DatabaseName is null ? host ?? "" : $"{host} / {target.DatabaseName}";
    }

    #endregion

    #region Problem and What to do

    private static (string Problem, string WhatToDo) Explain(StageReason reason, StageState stage) => reason switch {
        StageReason.NoOrgConnection => (
            "There is no Org Connection yet.",
            "Create a Signing Keypair, install its Signing Certificate on an External Client App in Salesforce, then Connect."),
        StageReason.OrgConnectionIncomplete => (
            "The Org Connection has a Signing Keypair but has never connected.",
            "Install the Signing Certificate on the External Client App in Salesforce, then Connect."),
        StageReason.OrgConnectionFailed => (
            "The Org Connection has Failed: Salesforce no longer accepts its sign-in.",
            "Open the Org Connection to see Salesforce's error. Check that the External Client App is still installed, " +
            "the Run-as User is active and the Signing Certificate has not expired, then Connect again."),
        StageReason.NoPrimaryChannel => (
            "No Primary Channel is chosen, so the worker has nothing to follow.",
            "Choose the Channel the worker should subscribe to."),
        StageReason.PrimaryChannelHasNoMembers => (
            "The Primary Channel carries no Entities, so no events will arrive.",
            "Add a Channel Member for each Entity you want to sync."),
        StageReason.CheckpointNearExpiry when stage is PrimaryChannelStage { CheckpointAge: { } age, CheckpointExpiresIn: { } left } => (
            $"The Checkpoint is {Duration(age)} old and expires in {Duration(left)}.",
            "The worker is not advancing it. Check that the worker is running and the Org Connection works. " +
            "Once the Checkpoint expires, the worker restarts from Earliest."),
        StageReason.CheckpointNearExpiry => (
            "The Checkpoint is close to Salesforce's 72-hour retention.",
            "The worker is not advancing it. Check that the worker is running and the Org Connection works."),
        StageReason.CheckpointExpired => (
            "The Checkpoint is older than Salesforce's 72-hour retention.",
            "The worker will restart from Earliest, and events older than 72 hours are lost. " +
            "Check why the worker stopped advancing the Checkpoint."),
        StageReason.NoTargetConnection => (
            "There is no Target Connection yet, so events have nowhere to land.",
            "Enter the Target Database's engine and details. They are proved by reading schema metadata when you save."),
        StageReason.TargetConnectionNotProved => (
            "The Target Connection was saved but has never been proved.",
            "Open it to see why the proof failed, correct the details and save again."),
        StageReason.TargetConnectionFailed => (
            "The Target Connection's last proof failed, so the worker cannot write to the Target Database.",
            "Open it to see the driver's error. Check that the database is reachable and the credentials still work, then prove it again."),
        StageReason.NoActiveBindings => (
            "No Binding is Active, so no events are written.",
            "Bind an Entity on the Primary Channel to a Target Table and activate it."),
        StageReason.BindingForcedIncomplete when stage is BindingsStage { Counts.ForcedIncomplete: var n } => (
            $"{Plural(n, "Binding")} {(n == 1 ? "was" : "were")} forced back to Incomplete and {(n == 1 ? "is" : "are")} not syncing: " +
            "an edit or a change to the Target Table left " + (n == 1 ? "it" : "them") + " invalid, or a Key Mapping column lost its unique constraint.",
            "Open each Binding marked Needs attention to see what blocks it, fix that, then activate it again."),
        _ => ("", "")
    };

    private static string ActionLabelFor(StageState stage) => (stage.Stage, stage.Status) switch {
        (PipelineStage.OrgConnection, StageStatus.NeedsAttention) => "Repair Org Connection",
        (PipelineStage.OrgConnection, _) => "Set up Org Connection",
        (PipelineStage.PrimaryChannel, _) when stage.Reasons.Contains(StageReason.NoPrimaryChannel) => "Choose Primary Channel",
        (PipelineStage.PrimaryChannel, _) => "Open Channels",
        (PipelineStage.TargetConnection, StageStatus.NeedsAttention) => "Repair Target Connection",
        (PipelineStage.TargetConnection, _) => "Set up Target Connection",
        (PipelineStage.Bindings, StageStatus.NeedsAttention) => "Open Bindings",
        _ => "Create a Binding"
    };

    #endregion

    #region Formatting

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    private static string Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url ?? "";

    private static string Ago(DateTimeOffset now, DateTime at) =>
        Duration(now - new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))) + " ago";

    /// <summary>
    /// Hours up to three days, because that is the scale the Checkpoint's retention is measured in. Rounded, not
    /// truncated, so an age and the time left of the same Checkpoint add up to 72 h.
    /// </summary>
    internal static string Duration(TimeSpan span) => span switch {
        { TotalMinutes: < 1 } => "under a minute",
        { TotalMinutes: < 59.5 } => $"{Math.Round(span.TotalMinutes)} min",
        { TotalHours: < 71.5 } => $"{Math.Round(span.TotalHours)} h",
        _ => Plural((int)Math.Round(span.TotalDays), "day")
    };

    #endregion
}
