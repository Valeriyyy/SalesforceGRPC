using Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

public class OverviewController(IPipelineStatusService pipeline, TimeProvider time) : PageController(pipeline) {
    [HttpGet("/")]
    public Task<IActionResult> Index(CancellationToken cancellationToken) =>
        SveltePageAsync("overview", "Overview", NavKey.Overview,
            status => OverviewView.For(status, time.GetUtcNow()), cancellationToken);
}
