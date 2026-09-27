using Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

public class TargetConnectionPageController(IPipelineStatusService pipeline) : PageController(pipeline) {
    [HttpGet("/target-connection")]
    public Task<IActionResult> Index(CancellationToken cancellationToken) =>
        PlaceholderAsync(NavKey.TargetConnection, "Target Connection", "Point the application at the Target Database events are written to.", cancellationToken);
}
