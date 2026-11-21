using Database.Models;
using System.Text.Json;

namespace StarPush.ViewModels;

/// <summary>
/// One Channel's page. <see cref="Channel"/> is null when there is no such Change Data Capture Channel, so the
/// page can say so inside the shell.
/// </summary>
public sealed record ChannelView(bool OrgConnectionReady, ChannelDetailView? Channel, ChannelsNotice? Notice) {
    public static ChannelView For(bool orgConnectionReady, PlatformEventChannelEntity? channel,
        IReadOnlyDictionary<int, CDCSchema> bindings, Checkpoint? checkpoint, DateTimeOffset now,
        ChannelsNotice? notice) {
        if (channel is null || !channel.IsChangeDataCapture) {
            return new ChannelView(orgConnectionReady, null, notice);
        }

        return new ChannelView(orgConnectionReady, new ChannelDetailView(
            channel.Id,
            channel.MasterLabel ?? channel.DeveloperName,
            channel.FullName,
            channel.IsPrimary,
            channel.StartingPoint,
            checkpoint is null ? null : new CheckpointView(
                checkpoint.SavedAt,
                StageView.Duration(checkpoint.Age(now)),
                StageView.Duration(checkpoint.ExpiresIn(now)),
                checkpoint.IsExpired(now),
                CanResume: !checkpoint.IsExpired(now)),
            channel.Members
                .OrderBy(m => m.SelectedEntity, StringComparer.OrdinalIgnoreCase)
                .Select(m => new ChannelMemberView(
                    m.Id,
                    m.SelectedEntity,
                    m.FilterExpression,
                    EnrichedFields(m.EnrichedFields),
                    m.CdcSchemaId is { } id && bindings.TryGetValue(id, out var b)
                        ? new MemberBindingView(b.Id, b.DbSchemaFullName, b.BindingState)
                        : null))
                .ToList()), notice);
    }

    private static IReadOnlyList<string> EnrichedFields(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return [];
        }
        try {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        } catch (JsonException) {
            return [];
        }
    }
}
