using DTO;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;

namespace StarPush.ViewModels;

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
