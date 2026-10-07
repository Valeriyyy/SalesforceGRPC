using Application.Services.Interfaces;
using Database.Models;
using DTO;
using Microsoft.AspNetCore.Mvc;
using Salesforce.Auth;
using Salesforce.Clients;
using Salesforce.Dtos;
using SalesforceGrpc.ViewModels;
using System.ComponentModel.DataAnnotations;

namespace SalesforceGrpc.Controllers;

/// <summary>
/// Manages Salesforce platform event channels and the events carried on them.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PlatformEventManagementController : Controller {
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

    /// <summary>
    /// Creates a Change Data Capture Channel from the Channels page — or adopts one Salesforce already has — with
    /// its Starting Point, optionally making it the Primary Channel. The Channel's page says which happened.
    /// </summary>
    [HttpPost("channels/data")]
    public Task<ActionResult<NewChannelResultDTO>> CreateDataChannel([FromBody] NewChannelDTO request,
        CancellationToken cancellationToken) =>
        Execute(async () => {
            var result = await _platformEventService.CreateDataChannelAsync(request, cancellationToken).ConfigureAwait(false);
            ChannelsNotice.Success(result.Adopted
                ? $"Salesforce already had {result.FullName}, so it was adopted with its {result.MemberCount} Channel Member{(result.MemberCount == 1 ? "" : "s")} rather than created."
                : $"Created {result.FullName}.").Put(TempData);
            return result;
        });

    [HttpPatch("channels/{id:int}")]
    public Task<ActionResult<PlatformEventChannelEntity>> UpdateChannel(int id, [FromBody] UpdateChannelDTO request,
        CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.UpdateChannelAsync(id, request, cancellationToken));

    [HttpDelete("channels/{id:int}")]
    public Task<ActionResult> DeleteChannel(int id, CancellationToken cancellationToken) =>
        Execute(async () => {
            var channel = await _platformEventService.GetChannelAsync(id, cancellationToken).ConfigureAwait(false);
            await _platformEventService.DeleteChannelAsync(id, cancellationToken).ConfigureAwait(false);
            ChannelsNotice.Success(channel?.IsPrimary == true
                ? $"Deleted {channel.FullName}. There is no Primary Channel now, so nothing is streaming."
                : $"Deleted {channel?.FullName}.").Put(TempData);
        });

    [HttpGet("channels/{id:int}/members")]
    public Task<ActionResult<List<PlatformEventChannelMemberEntity>>> GetChannelMembers(int id,
        CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.GetChannelMembersAsync(id, cancellationToken));

    [HttpPost("channels/{id:int}/members")]
    public Task<ActionResult<PlatformEventChannelMemberEntity>> AddChannelMember(int id,
        [FromBody] CreateChannelMemberDTO request, CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.AddChannelMemberAsync(id, request, cancellationToken));

    /// <summary>
    /// Adds several Entities to a channel, all or none. Answers 200 either way: <c>added</c> says which, and each
    /// Entity's outcome names the one Salesforce refused.
    /// </summary>
    [HttpPost("channels/{id:int}/members/batch")]
    public Task<ActionResult<AddChannelMembersResultDTO>> AddChannelMembers(int id,
        [FromBody] AddChannelMembersDTO request, CancellationToken cancellationToken) =>
        Execute(() => _platformEventService.AddChannelMembersAsync(id, request, cancellationToken));

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
    public Task<ActionResult<ResyncReportDTO>> Resync(CancellationToken cancellationToken) =>
        Execute(async () => {
            var report = await _platformEventService.ResyncFromSalesforceAsync(cancellationToken).ConfigureAwait(false);
            ChannelsNotice.ForResync(report).Put(TempData);
            return report;
        });

    /// <summary>
    /// Runs an action, mapping the failure modes onto status codes: a rejected request is the caller's
    /// fault (400), an unknown ID is a 404, and a Salesforce refusal is an upstream failure (502). Bodies take
    /// the shapes every api controller answers with — <c>{error}</c>, or for Salesforce
    /// <c>{error, errorDescription, guidance, rawResponse}</c> — so the pages show them one way.
    /// </summary>
    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action) {
        try {
            return Ok(await action().ConfigureAwait(false));
        } catch (ValidationException ex) {
            return BadRequest(new { error = ex.Message });
        } catch (KeyNotFoundException ex) {
            return NotFound(new { error = ex.Message });
        } catch (SalesforceToolingException ex) {
            _logger.LogError(ex, "Salesforce rejected a platform event channel operation");
            return StatusCode(StatusCodes.Status502BadGateway, new {
                error = ex.Errors.FirstOrDefault()?.ErrorCode ?? $"HTTP {(int)ex.StatusCode}",
                errorDescription = string.Join(" ", ex.Errors.Select(e => e.Message)),
                guidance = (string?)null,
                rawResponse = ex.RawBody ?? ex.Message
            });
        } catch (NoOrgConnectionException ex) {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Unexpected failure handling a platform event channel operation");
            return BadRequest(new { error = ex.Message });
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
