using Database.Models;

namespace StarPush.ViewModels;

/// <summary>
/// The Channels list: every Change Data Capture Channel, which one is Primary, and when the Mirror was last
/// synced from Salesforce.
/// </summary>
/// <remarks>
/// Platform event channels are mirrored but never shown: this application only streams Change Data Capture.
/// </remarks>
public sealed record ChannelsView(
    bool OrgConnectionReady,
    IReadOnlyList<ChannelRowView> Channels,
    DateTime? LastSyncedAt,
    ChannelsNotice? Notice) {

    public static ChannelsView For(bool orgConnectionReady, IEnumerable<PlatformEventChannelEntity> channels,
        IReadOnlyDictionary<int, Checkpoint> checkpoints, DateTimeOffset now, ChannelsNotice? notice) {
        var shown = channels.Where(c => c.IsChangeDataCapture).ToList();

        return new ChannelsView(
            orgConnectionReady,
            shown.Select(c => new ChannelRowView(
                c.Id,
                c.MasterLabel ?? c.DeveloperName,
                c.FullName,
                c.IsPrimary,
                c.Members.Count,
                c.Members.Count(m => m.CdcSchemaId is null),
                c.IsPrimary ? Streaming(c, checkpoints.GetValueOrDefault(c.Id), now) : null)).ToList(),
            shown.Max(c => c.LastSyncedAt),
            notice);
    }

    private static string Streaming(PlatformEventChannelEntity channel, Checkpoint? checkpoint, DateTimeOffset now) =>
        checkpoint is null
            ? $"No Checkpoint yet · starts at {channel.StartingPoint}"
            : checkpoint.IsExpired(now)
                ? $"Checkpoint expired {StageView.Duration(checkpoint.Age(now))} ago · restarts from Earliest"
                : $"Checkpoint {StageView.Duration(checkpoint.Age(now))} ago, expires in {StageView.Duration(checkpoint.ExpiresIn(now))}";
}
