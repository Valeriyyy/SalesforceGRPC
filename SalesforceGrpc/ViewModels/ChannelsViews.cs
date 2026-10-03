using Database.Models;
using DTO;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;

namespace SalesforceGrpc.ViewModels;

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

/// <summary>One Channel on the list. <see cref="Streaming"/> is set for the Primary Channel only.</summary>
public sealed record ChannelRowView(
    int Id,
    string Label,
    string FullName,
    bool IsPrimary,
    int MemberCount,
    int UnboundCount,
    string? Streaming);

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

public sealed record ChannelDetailView(
    int Id,
    string Label,
    string FullName,
    bool IsPrimary,
    StartingPoint StartingPoint,
    CheckpointView? Checkpoint,
    IReadOnlyList<ChannelMemberView> Members);

/// <summary>
/// A Channel's Checkpoint, for the Resume choice and the note beside the Starting Point. Ages are already in
/// words, on the same hour scale the Overview uses.
/// </summary>
public sealed record CheckpointView(DateTime SavedAt, string Age, string ExpiresIn, bool Expired, bool CanResume);

public sealed record ChannelMemberView(
    int Id,
    string Entity,
    string? FilterExpression,
    IReadOnlyList<string> EnrichedFields,
    MemberBindingView? Binding);

/// <summary>The Binding a Channel Member's Entity lands through.</summary>
public sealed record MemberBindingView(int Id, string TargetTable, BindingState State);

/// <summary>Creating a Channel. Making it the Primary Channel is the default only when there is none.</summary>
public sealed record NewChannelView(bool OrgConnectionReady, string? PrimaryChannelFullName, bool MakePrimaryByDefault) {
    public static NewChannelView For(bool orgConnectionReady, string? primaryChannelFullName) =>
        new(orgConnectionReady, primaryChannelFullName, MakePrimaryByDefault: primaryChannelFullName is null);
}

/// <summary>
/// What an action on the Channels pages did, shown once on the page it lands on: a Resync's report, an adopted
/// Channel, a deletion.
/// </summary>
/// <remarks>
/// It travels through TempData from the api call to the page load that follows it, the way
/// <see cref="OrgConnectionNotice"/> does, so a page that reloads after an action can still say what happened.
/// </remarks>
public sealed record ChannelsNotice(NoticeKind Kind, string Message, ResyncReportDTO? Resync) {
    private const string TempDataKey = "ChannelsNotice";

    public void Put(ITempDataDictionary tempData) => tempData[TempDataKey] = JsonSerializer.Serialize(this);

    public static ChannelsNotice? Take(ITempDataDictionary tempData) =>
        tempData[TempDataKey] is string json ? JsonSerializer.Deserialize<ChannelsNotice>(json) : null;

    public static ChannelsNotice Success(string message) => new(NoticeKind.Success, message, null);

    /// <summary>Summarises a Resync in one line, keeping the report for the detail.</summary>
    public static ChannelsNotice ForResync(ResyncReportDTO report) => report switch {
        { PrimaryChannelRemoved: { } primary } => new(NoticeKind.Error,
            $"Resync found the Primary Channel {primary} deleted in Salesforce. Nothing is streaming until you choose another.",
            report),
        { HasChanges: false } => new(NoticeKind.Success, "Resync found nothing changed in Salesforce.", report),
        _ => new(NoticeKind.Success, "Resync brought in what changed in Salesforce.", report)
    };
}
