using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// The Metadata envelope for a PlatformEventChannelMember. PATCH requires the complete object —
/// partial definitions are not supported, and supplied enrichedFields replace the existing set.
/// </summary>
public class PlatformEventChannelMemberMetadata {
    /// <summary>
    /// Required. The channel's full name with double underscores intact, e.g. "SalesEvents__chn",
    /// or "ChangeEvents" for the standard channel. Immutable after create.
    /// </summary>
    [JsonProperty("eventChannel")]
    public string? EventChannel { get; set; }

    /// <summary>
    /// Required. The event/entity name, e.g. "AccountChangeEvent" or "Order_Event__e".
    /// Immutable after create.
    /// </summary>
    [JsonProperty("selectedEntity")]
    public string? SelectedEntity { get; set; }

    /// <summary>Optional, API 51.0+. Fields always included in the payload even when unchanged.</summary>
    [JsonProperty("enrichedFields")]
    public List<EnrichedField>? EnrichedFields { get; set; }

    /// <summary>Optional, API 56.0+. Server-side delivery filter.</summary>
    [JsonProperty("filterExpression")]
    public string? FilterExpression { get; set; }
}
