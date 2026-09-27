using Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

public class OrgConnectionPageController(IPipelineStatusService pipeline) : PageController(pipeline) {
    [HttpGet("/org-connection")]
    public Task<IActionResult> Index(CancellationToken cancellationToken) =>
        PlaceholderAsync(NavKey.OrgConnection, "Org Connection", "Connect this application to your Salesforce org.", cancellationToken);
}
