using Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

public class ChannelsPageController(IPipelineStatusService pipeline) : PageController(pipeline) {
    [HttpGet("/channels")]
    public Task<IActionResult> Index(CancellationToken cancellationToken) =>
        PlaceholderAsync(NavKey.Channels, "Channels", "Choose the Primary Channel and the Entities it carries.", cancellationToken);
}
