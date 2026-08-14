using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// The subset of an sObject describe response this application needs. Used to read the
/// PlatformEventChannelMember.SelectedEntity picklist, which is the authoritative list of entities
/// that may be added to a channel.
/// </summary>
public class ToolingDescribeResponse {
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("fields")]
    public List<ToolingDescribeField>? Fields { get; set; }
}

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

public class ToolingPicklistValue {
    [JsonProperty("value")]
    public string? Value { get; set; }

    [JsonProperty("label")]
    public string? Label { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; }
}
