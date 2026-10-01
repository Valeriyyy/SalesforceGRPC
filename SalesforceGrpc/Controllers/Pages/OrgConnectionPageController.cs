using Application.Connections;
using Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

public class OrgConnectionPageController(IPipelineStatusService pipeline, IOrgConnectionService connections)
    : PageController(pipeline) {
    [HttpGet("/org-connection")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) {
        var connection = await connections.GetAsync(cancellationToken).ConfigureAwait(false);
        var notice = OrgConnectionNotice.Take(TempData);

        return await SveltePageAsync("org-connection", "Org Connection", NavKey.OrgConnection,
            _ => OrgConnectionView.For(connection, notice), cancellationToken).ConfigureAwait(false);
    }
}
