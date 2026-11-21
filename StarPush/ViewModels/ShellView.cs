using Application.Pipeline;

namespace StarPush.ViewModels;

/// <summary>What the left-nav shell needs on every page.</summary>
public sealed record ShellView(string AppName, NavKey CurrentNav, IReadOnlyList<NavItemView> Nav) {
    public const string ApplicationName = "StarPush";

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
