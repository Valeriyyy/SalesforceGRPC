using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// An entry in PlatformEventChannelMember.Metadata.enrichedFields — a field Salesforce always includes
/// in the change event payload, even when its value did not change.
/// </summary>
public class EnrichedField {
    [JsonProperty("name")]
    public string? Name { get; set; }
}
