using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// The PlatformEventChannel Tooling API object — a custom channel (ID prefix <c>0YL</c>).
/// </summary>
/// <remarks>
/// <see cref="FullName"/> and <see cref="Metadata"/> are only returned when a SOQL query resolves to a
/// single record; list queries must omit them and use the retrieve endpoint for full detail.
/// </remarks>
public class PlatformEventChannel {
    [JsonProperty("Id")]
    public string? Id { get; set; }

    /// <summary>"data" for Change Data Capture, "event" for platform events. Immutable after create.</summary>
    [JsonProperty("ChannelType")]
    public string? ChannelType { get; set; }

    /// <summary>The unique name without the <c>__chn</c> suffix. Derived by Salesforce from FullName.</summary>
    [JsonProperty("DeveloperName")]
    public string? DeveloperName { get; set; }

    /// <summary>"custom", "data", "monitoring" or "standard" (API 61.0+). Immutable after create.</summary>
    [JsonProperty("EventType")]
    public string? EventType { get; set; }

    /// <summary>The metadata full name including the <c>__chn</c> suffix, e.g. "SalesEvents__chn".</summary>
    [JsonProperty("FullName")]
    public string? FullName { get; set; }

    [JsonProperty("Language")]
    public string? Language { get; set; }

    [JsonProperty("ManageableState")]
    public string? ManageableState { get; set; }

    [JsonProperty("MasterLabel")]
    public string? MasterLabel { get; set; }

    [JsonProperty("Metadata")]
    public PlatformEventChannelMetadata? Metadata { get; set; }

    [JsonProperty("NamespacePrefix")]
    public string? NamespacePrefix { get; set; }

    /// <summary>
    /// Not part of the Salesforce payload — populated by this application from a separate
    /// PlatformEventChannelMember query.
    /// </summary>
    [JsonIgnore]
    public List<PlatformEventChannelMember>? Members { get; set; }
}
