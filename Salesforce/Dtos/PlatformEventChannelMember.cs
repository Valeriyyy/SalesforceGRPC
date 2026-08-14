using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// The PlatformEventChannelMember Tooling API object — one event/entity on a channel (ID prefix <c>0v8</c>).
/// </summary>
/// <remarks>
/// In SOQL results <see cref="EventChannel"/> comes back as the channel ID (<c>0YL…</c>) and
/// <see cref="SelectedEntity"/> as an EntityDefinition ID (<c>01I…</c>), not as names. Readable names are
/// only available from <see cref="Metadata"/> on the retrieve endpoint, which is why writes are followed
/// by a retrieve before the result is mirrored locally.
/// </remarks>
public class PlatformEventChannelMember {
    [JsonProperty("Id")]
    public string? Id { get; set; }

    /// <summary>The channel ID in query results; the channel name in Metadata. Immutable after create.</summary>
    [JsonProperty("EventChannel")]
    public string? EventChannel { get; set; }

    [JsonProperty("MasterLabel")]
    public string? MasterLabel { get; set; }

    /// <summary>Format <c>ChannelName_EventName</c>, e.g. "SalesEvents_chn_AccountChangeEvent".</summary>
    [JsonProperty("DeveloperName")]
    public string? DeveloperName { get; set; }

    /// <summary>SOQL-subset filter applied by Salesforce before delivery. API 56.0+. Updatable.</summary>
    [JsonProperty("FilterExpression")]
    public string? FilterExpression { get; set; }

    /// <summary>An EntityDefinition ID in query results; the entity name in Metadata. Immutable after create.</summary>
    [JsonProperty("SelectedEntity")]
    public string? SelectedEntity { get; set; }

    [JsonProperty("FullName")]
    public string? FullName { get; set; }

    [JsonProperty("ManageableState")]
    public string? ManageableState { get; set; }

    [JsonProperty("Metadata")]
    public PlatformEventChannelMemberMetadata? Metadata { get; set; }

    [JsonProperty("NamespacePrefix")]
    public string? NamespacePrefix { get; set; }
}
