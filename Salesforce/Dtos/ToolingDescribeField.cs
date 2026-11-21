using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class ToolingDescribeField {
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("label")]
    public string? Label { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("picklistValues")]
    public List<ToolingPicklistValue>? PicklistValues { get; set; }
}
