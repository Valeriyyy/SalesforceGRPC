using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class PlatformEventChannel {
    [JsonProperty("Id")]
    public string? Id { get; set; }
    [JsonProperty("ChannelType")]
    public string? ChannelType { get; set; }
    [JsonProperty("DeveloperName")]
    public string? DeveloperName { get; set; }
    [JsonProperty("EventType")]
    public string? EventType { get; set; }
    [JsonProperty("FullName")]
    public string? FullName { get; set; }
    [JsonProperty("Language")]
    public string? Language { get; set; }
    [JsonProperty("ManageableState")]
    public string? ManageableState { get; set; }
    [JsonProperty("MasterLabel")]
    public string? MasterLabel { get; set; }
    [JsonProperty("Metadata")]
    public object? Metadata { get; set; }
    [JsonProperty("NamespacePrefix")]
    public string NamespacePrefix { get; set; }

    public List<PlatformEventChannelMember> Members { get; set; }  
}