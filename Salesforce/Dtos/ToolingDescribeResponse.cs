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
