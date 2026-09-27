using Application.Pipeline;

namespace SalesforceGrpc.ViewModels;

/// <summary>The sidebar entries, in the order they are listed.</summary>
public enum NavKey {
    Overview,
    OrgConnection,
    Channels,
    TargetConnection,
    Bindings
}

/// <summary>What the left-nav shell needs on every page.</summary>
public sealed record ShellView(string AppName, NavKey CurrentNav, IReadOnlyList<NavItemView> Nav) {
    public const string ApplicationName = "SalesforceGRPC";

    /// <summary>
    /// Builds the sidebar from the same <see cref="PipelineStatus"/> the page uses, so they cannot disagree.
    /// </summary>
    public static ShellView For(NavKey current, PipelineStatus pipeline) => new(ApplicationName, current, [
        new NavItemView(NavKey.Overview, "Overview", StagePages.OverviewHref, null, null),
        Step(NavKey.OrgConnection, "Org Connection", pipeline.OrgConnection),
        Step(NavKey.Channels, "Channels", pipeline.PrimaryChannel),
        Step(NavKey.TargetConnection, "Target Connection", pipeline.TargetConnection),
        Step(NavKey.Bindings, "Bindings", pipeline.Bindings)
    ]);

    private static NavItemView Step(NavKey key, string label, StageState stage) =>
        new(key, label, StagePages.Href(stage.Stage), StagePages.StepNumber(stage.Stage), stage.Status);
}

/// <summary>One sidebar entry. Overview has no step number and no status.</summary>
public sealed record NavItemView(NavKey Key, string Label, string Href, int? StepNumber, StageStatus? Status);

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
