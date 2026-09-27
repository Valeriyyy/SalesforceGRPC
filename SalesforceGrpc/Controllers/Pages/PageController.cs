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
        var status = await pipeline.GetAsync(cancellationToken).ConfigureAwait(false);
        var view = new PageView<TPage>(ShellView.For(nav, status), page(status));
        return View("SveltePage", SveltePage.For(component, title, view));
    }

    /// <summary>A Stage page that is not built yet, inside the shell, so links to it do not 404.</summary>
    protected Task<IActionResult> PlaceholderAsync(NavKey nav, string title, string description,
        CancellationToken cancellationToken) =>
        SveltePageAsync("placeholder", title, nav, _ => new PlaceholderView(title, description), cancellationToken);
}
