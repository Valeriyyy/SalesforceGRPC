using Application.Pipeline;
using Application.Targets;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

public class TargetConnectionPageController(IPipelineStatusService pipeline, ITargetConnectionService connections)
    : PageController(pipeline) {
    [HttpGet("/target-connection")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) {
        var connection = await connections.GetAsync(cancellationToken).ConfigureAwait(false);
        var counts = await connections.PreviewRepointAsync(cancellationToken).ConfigureAwait(false);
        var engines = connections.GetEngines();

        return await SveltePageAsync("target-connection", "Target Connection", NavKey.TargetConnection,
            status => TargetConnectionView.For(connection, engines, counts, status.OrgConnection.Status == StageStatus.Ok),
            cancellationToken).ConfigureAwait(false);
    }
}
