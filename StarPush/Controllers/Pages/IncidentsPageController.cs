using Application.Pipeline;
using Database.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using StarPush.ViewModels;

namespace StarPush.Controllers.Pages;

/// <summary>The Incidents of the last 24 hours, linked from the Overview's worker card. See ADR 0006.</summary>
public class IncidentsPageController(IPipelineStatusService pipeline, IIncidentRepository incidents, TimeProvider time)
    : PageController(pipeline) {

    [HttpGet("/incidents")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) {
        var since = (time.GetUtcNow() - IIncidentRepository.Retention).UtcDateTime;
        var recent = await incidents.ListSinceAsync(since, cancellationToken).ConfigureAwait(false);
        return await SveltePageAsync("incidents", "Incidents", NavKey.Overview,
            _ => new IncidentsView(recent.Select(IncidentView.For).ToList()), cancellationToken).ConfigureAwait(false);
    }
}
