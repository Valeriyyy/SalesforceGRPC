using Application.Pipeline;
using Application.Services.Interfaces;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.ViewModels;

namespace SalesforceGrpc.Controllers.Pages;

/// <summary>
/// The Channels pages: the list, creating a Channel, and one Channel's own page. Every read comes from the Mirror,
/// so no page load waits on Salesforce.
/// </summary>
public class ChannelsPageController(IPipelineStatusService pipeline, IPlatformEventService channels,
    IMetaRepository bindings, ICheckpointRepository checkpoints, TimeProvider time) : PageController(pipeline) {

    [HttpGet("/channels")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) {
        var notice = ChannelsNotice.Take(TempData);
        var all = await LoadChannelsAsync(cancellationToken).ConfigureAwait(false);

        var saved = new Dictionary<int, Checkpoint>();
        foreach (var channel in all.Where(c => c.IsPrimary)) {
            if (await checkpoints.GetAsync(channel.Id, cancellationToken).ConfigureAwait(false) is { } checkpoint) {
                saved[channel.Id] = checkpoint;
            }
        }

        return await SveltePageAsync("channels", "Channels", NavKey.Channels,
            status => ChannelsView.For(OrgConnectionReady(status), all, saved, time.GetUtcNow(), notice),
            cancellationToken).ConfigureAwait(false);
    }

    [HttpGet("/channels/new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken) {
        var all = await channels.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
        var primary = all.FirstOrDefault(c => c.IsPrimary)?.FullName;

        return await SveltePageAsync("channel-new", "New Channel", NavKey.Channels,
            status => NewChannelView.For(OrgConnectionReady(status), primary), cancellationToken).ConfigureAwait(false);
    }

    [HttpGet("/channels/{id:int}")]
    public async Task<IActionResult> Show(int id, CancellationToken cancellationToken) {
        var notice = ChannelsNotice.Take(TempData);
        var channel = await channels.GetChannelAsync(id, cancellationToken).ConfigureAwait(false);
        var checkpoint = channel is null ? null : await checkpoints.GetAsync(channel.Id, cancellationToken).ConfigureAwait(false);
        var bound = (await bindings.GetCachedSchemas(cancellationToken).ConfigureAwait(false)).ToDictionary(b => b.Id);

        return await SveltePageAsync("channel", channel?.MasterLabel ?? "Channel", NavKey.Channels,
            status => ChannelView.For(OrgConnectionReady(status), channel, bound, checkpoint, time.GetUtcNow(), notice),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every channel with its members. Orgs are capped at 100 channels, so one read each is bounded.</summary>
    private async Task<List<PlatformEventChannelEntity>> LoadChannelsAsync(CancellationToken cancellationToken) {
        var loaded = new List<PlatformEventChannelEntity>();
        foreach (var channel in await channels.GetChannelsAsync(cancellationToken).ConfigureAwait(false)) {
            loaded.Add(await channels.GetChannelAsync(channel.Id, cancellationToken).ConfigureAwait(false) ?? channel);
        }
        return loaded;
    }

    /// <summary>Every channel action needs the Administering User, so nothing is offered until the org is connected.</summary>
    private static bool OrgConnectionReady(PipelineStatus status) =>
        status.Stages.OfType<OrgConnectionStage>().Single().Status == StageStatus.Ok;
}
