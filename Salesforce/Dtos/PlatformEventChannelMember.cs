using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class PlatformEventChannelMember {
    [JsonProperty("Id")]
    public string? Id { get; set; }
    [JsonProperty("EventChannel")]
    public string? EventChannel { get; set; }
    [JsonProperty("MasterLabel")]
    public string? MasterLabel { get; set; }
    [JsonProperty("DeveloperName")]
    public string? DeveloperName { get; set; }
    [JsonProperty("FilterExpression")]
    public string? FilterExpression { get; set; }
    [JsonProperty("SelectedEntity")]
    public string? SelectedEntity { get; set; }
}