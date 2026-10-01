using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;

namespace SalesforceGrpc.ViewModels;

public enum NoticeKind {
    Success,
    Error
}

/// <summary>
/// What happened on the way back from Salesforce, shown once on the Org Connection page.
/// </summary>
/// <remarks>
/// Only for outcomes the connection does not record itself — a refused approval, a callback this application
/// never started, a failed code exchange, an org mismatch, or plain success. It travels from the callback to the
/// page through TempData, so it survives exactly one redirect.
/// </remarks>
public sealed record OrgConnectionNotice(NoticeKind Kind, string Message, SalesforceErrorView? Error,
    OrgMismatchView? OrgMismatch) {

    private const string TempDataKey = "OrgConnectionNotice";

    /// <summary>Leaves this notice for the next request, which is the redirect to the Org Connection page.</summary>
    public void Put(ITempDataDictionary tempData) => tempData[TempDataKey] = JsonSerializer.Serialize(this);

    /// <summary>The notice left by the previous request, if any. Reading it consumes it.</summary>
    public static OrgConnectionNotice? Take(ITempDataDictionary tempData) =>
        tempData[TempDataKey] is string json ? JsonSerializer.Deserialize<OrgConnectionNotice>(json) : null;

    public static OrgConnectionNotice Success(string message) => new(NoticeKind.Success, message, null, null);

    public static OrgConnectionNotice Failure(string message) => new(NoticeKind.Error, message, null, null);

    public static OrgConnectionNotice SalesforceFailure(string message, SalesforceErrorView error) =>
        new(NoticeKind.Error, message, error, null);

    public static OrgConnectionNotice Mismatch(string message, string storedOrgId, string discoveredOrgId) =>
        new(NoticeKind.Error, message, null, new OrgMismatchView(storedOrgId, discoveredOrgId));
}

/// <summary>Both org ids, because that is the first thing anyone asks when a connection is refused for one.</summary>
public sealed record OrgMismatchView(string StoredOrgId, string DiscoveredOrgId);
