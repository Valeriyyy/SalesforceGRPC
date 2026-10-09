using Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

/// <summary>
/// A view controller: builds one page's view class and hands it to its Svelte component. See ADR 0006.
/// </summary>
/// <remarks>
/// Every page reads the <see cref="PipelineStatus"/> once, and both the shell and the page are built from that
/// one read, so the sidebar and the page can never disagree.
/// </remarks>
public abstract class PageController(IPipelineStatusService pipeline) : Controller {
    protected async Task<IActionResult> SveltePageAsync<TPage>(string component, string title, NavKey nav,
        Func<PipelineStatus, TPage> page, CancellationToken cancellationToken) {
        var status = await ReadPipelineAsync(cancellationToken).ConfigureAwait(false);
        return RenderPage(component, title, nav, status, page(status));
    }

    /// <summary>
    /// The one read of the Pipeline a page is built from, for an action that has to know where the Pipeline
    /// stands before deciding what to load. Pass it on to <see cref="RenderPage{TPage}"/>.
    /// </summary>
    protected Task<PipelineStatus> ReadPipelineAsync(CancellationToken cancellationToken) =>
        pipeline.GetAsync(cancellationToken);

    protected IActionResult RenderPage<TPage>(string component, string title, NavKey nav, PipelineStatus status,
        TPage page) {
        var view = new PageView<TPage>(ShellView.For(nav, status), page);
        return View("SveltePage", SveltePage.For(component, title, view));
    }

    /// <summary>A Stage page that is not built yet, inside the shell, so links to it do not 404.</summary>
    protected Task<IActionResult> PlaceholderAsync(NavKey nav, string title, string description,
        CancellationToken cancellationToken) =>
        SveltePageAsync("placeholder", title, nav, _ => new PlaceholderView(title, description), cancellationToken);
}
