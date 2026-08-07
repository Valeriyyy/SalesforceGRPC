using Microsoft.AspNetCore.Mvc;
using Salesforce.Dtos;
using SalesforceGrpc.Salesforce;

namespace SalesforceGrpc.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelController : ControllerBase {
    private readonly ILogger<ChannelController> _logger;
    private readonly SalesforceToolingClient _sfToolingClient;

    public ChannelController(ILogger<ChannelController> logger, SalesforceToolingClient sfToolingClient) {
        _logger = logger;
        _sfToolingClient = sfToolingClient;
    }
    
    [HttpGet("channels")]
    public async Task<ActionResult<List<PlatformEventChannelMember>>> GetChannels() {
        // var res = await _sfToolingClient.GetCDCChannelByDeveloperNameAsync("MyCustomChannel");
        var res = await _sfToolingClient.GetPlatformChannelEventMembers("ChangeEvents");
        return Ok(res);
    }
}