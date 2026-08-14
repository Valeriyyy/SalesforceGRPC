using Application.Services.Interfaces;
using Database.Models;
using DTO;
using Microsoft.AspNetCore.Mvc;
using Salesforce.Clients;
using Salesforce.Dtos;
using System.ComponentModel.DataAnnotations;

namespace SalesforceGrpc.Controllers;

/// <summary>
/// Manages Salesforce platform event channels and the events carried on them.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PlatformEventManagementController : ControllerBase {
    private readonly ILogger<PlatformEventManagementController> _logger;
    private readonly IPlatformEventService _platformEventService;

    public PlatformEventManagementController(ILogger<PlatformEventManagementController> logger,
        IPlatformEventService platformEventService) {
        _logger = logger;
        _platformEventService = platformEventService;
    }

    [HttpGet("channels")]
    public Task<ActionResult<List<PlatformEventChannelEntity>>> GetChannels(CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.GetChannelsAsync(cancellationToken));

    [HttpGet("channels/{id:int}")]
    public Task<ActionResult<PlatformEventChannelEntity>> GetChannel(int id, CancellationToken cancellationToken) =>
        Execute<PlatformEventChannelEntity>(async () =>
            await _platformEventService.GetChannelAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"No platform event channel with ID {id}."));

    [HttpPost("channels")]
    public Task<ActionResult<PlatformEventChannelEntity>> CreateChannel([FromBody] CreateChannelDTO request,
        CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.CreateChannelAsync(request, cancellationToken));

    [HttpPatch("channels/{id:int}")]
    public Task<ActionResult<PlatformEventChannelEntity>> UpdateChannel(int id, [FromBody] UpdateChannelDTO request,
        CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.UpdateChannelAsync(id, request, cancellationToken));

    [HttpDelete("channels/{id:int}")]
    public Task<ActionResult> DeleteChannel(int id, CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.DeleteChannelAsync(id, cancellationToken));

    [HttpGet("channels/{id:int}/members")]
    public Task<ActionResult<List<PlatformEventChannelMemberEntity>>> GetChannelMembers(int id,
        CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.GetChannelMembersAsync(id, cancellationToken));

    [HttpPost("channels/{id:int}/members")]
    public Task<ActionResult<PlatformEventChannelMemberEntity>> AddChannelMember(int id,
        [FromBody] CreateChannelMemberDTO request, CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.AddChannelMemberAsync(id, request, cancellationToken));

    [HttpPatch("members/{memberId:int}")]
    public Task<ActionResult<PlatformEventChannelMemberEntity>> UpdateChannelMember(int memberId,
        [FromBody] UpdateChannelMemberDTO request, CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.UpdateChannelMemberAsync(memberId, request, cancellationToken));

    [HttpDelete("members/{memberId:int}")]
    public Task<ActionResult> RemoveChannelMember(int memberId, CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.RemoveChannelMemberAsync(memberId, cancellationToken));

    /// <summary>
    /// Lists the entities that can be added to a channel, for populating a picker.
    /// </summary>
    /// <param name="channelType">"data" for Change Data Capture entities, "event" for platform events.</param>
    [HttpGet("selectable-entities")]
    public Task<ActionResult<List<ToolingPicklistValue>>> GetSelectableEntities([FromQuery] string? channelType,
        CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.GetSelectableEntitiesAsync(channelType, cancellationToken));

    /// <summary>
    /// Rebuilds the local mirror from Salesforce, picking up changes made directly in Setup.
    /// </summary>
    [HttpPost("resync")]
    public Task<ActionResult<List<PlatformEventChannelEntity>>> Resync(CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.ResyncFromSalesforceAsync(cancellationToken));

    /// <summary>
    /// Runs an action, mapping the failure modes onto status codes: a rejected request is the caller's
    /// fault (400), an unknown ID is a 404, and a Salesforce refusal is an upstream failure (502).
    /// </summary>
    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action) {
        try {
            return Ok(await action().ConfigureAwait(false));
        } catch (ValidationException ex) {
            return BadRequest(ex.Message);
        } catch (KeyNotFoundException ex) {
            return NotFound(ex.Message);
        } catch (SalesforceToolingException ex) {
            _logger.LogError(ex, "Salesforce rejected a platform event channel operation");
            return StatusCode(StatusCodes.Status502BadGateway, ex.Errors.Count > 0 ? ex.Errors : (object)ex.Message);
        } catch (Exception ex) {
            _logger.LogError(ex, "Unexpected failure handling a platform event channel operation");
            return BadRequest(ex.Message);
        }
    }

    private async Task<ActionResult> Execute(Func<Task> action) {
        var result = await Execute<bool>(async () => {
            await action().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        return result.Result ?? Ok();
    }
}
