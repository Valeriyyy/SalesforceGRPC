using Application.Pipeline;
using Application.Runtime;
using Microsoft.AspNetCore.Mvc;
using StarPush.ViewModels;

namespace StarPush.Controllers.Pages;

public class OverviewController(IPipelineStatusService pipeline, IWorkerStatusService workers, TimeProvider time)
    : PageController(pipeline) {

    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) {
        var worker = await workers.GetAsync(cancellationToken).ConfigureAwait(false);
        return await SveltePageAsync("overview", "Overview", NavKey.Overview,
            status => OverviewView.For(status, worker, time.GetUtcNow()), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The worker card on its own, for the Overview to poll. Built by the same code as the page's, and serialized
    /// the same way (camelCase, enums as camelCase strings), so the card can swap one for the other.
    /// </summary>
    [HttpGet("/api/worker-status")]
    public async Task<IActionResult> WorkerStatus(CancellationToken cancellationToken) {
        var worker = await workers.GetAsync(cancellationToken).ConfigureAwait(false);
        return Json(WorkerStatusView.For(worker, time.GetUtcNow()), SveltePage.JsonOptions);
    }
}
