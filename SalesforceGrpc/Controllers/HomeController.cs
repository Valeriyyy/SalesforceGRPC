using Microsoft.AspNetCore.Mvc;

namespace SalesforceGrpc.Controllers;

public class HomeController : Controller {
    [HttpGet("/")]
    public IActionResult Index() {
        return View();
    }
}