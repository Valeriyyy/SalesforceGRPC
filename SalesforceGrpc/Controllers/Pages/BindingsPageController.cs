using Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

public class BindingsPageController(IPipelineStatusService pipeline) : PageController(pipeline) {
    [HttpGet("/bindings")]
    public Task<IActionResult> Index(CancellationToken cancellationToken) =>
        PlaceholderAsync(NavKey.Bindings, "Bindings", "Bind each Entity on the Primary Channel to a Target Table.", cancellationToken);
}
