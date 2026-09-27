using Microsoft.AspNetCore.Mvc;

namespace SalesforceGrpc.Controllers;

// PROTOTYPE — throwaway route for reviewing the shell and Overview design against fixture data.
// Delete with app/src/pages/overview-prototype once the design is settled.
public class OverviewPrototypeController(IWebHostEnvironment env) : Controller {
    [HttpGet("/prototype/overview")]
    public IActionResult Index() {
        if (env.IsProduction()) {
            return NotFound();
        }

        return View();
    }
}
