using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// The Metadata envelope for a PlatformEventChannel. On create, this is the only place channel
/// properties may be set — the sole createable top-level field is FullName.
/// </summary>
public class PlatformEventChannelMetadata {
    /// <summary>Required. "data" (Change Data Capture) or "event" (platform events).</summary>
    [JsonProperty("channelType")]
    public string? ChannelType { get; set; }

    /// <summary>Required. The display label, surfaced as MasterLabel.</summary>
    [JsonProperty("label")]
    public string? Label { get; set; }

    /// <summary>Optional, API 61.0+. "custom", "data" or "monitoring".</summary>
    [JsonProperty("eventType")]
    public string? EventType { get; set; }
}
