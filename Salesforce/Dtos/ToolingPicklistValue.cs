using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class ToolingPicklistValue {
    [JsonProperty("value")]
    public string? Value { get; set; }

    [JsonProperty("label")]
    public string? Label { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; }
}
