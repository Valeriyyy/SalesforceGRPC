using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class EnrichedField {
    [JsonProperty("Id")]
    public string? Id { get; set; }
    
    [JsonProperty("EventChannelId")]
    public string? EventChannelId { get; set; }
    
    [JsonProperty("EntityId")]
    public string? EntityId { get; set; }
    
    [JsonProperty("EntityFieldId")]
    public string? EntityFieldId { get; set; }
    
    [JsonProperty("IsSelected")]
    public bool IsSelected { get; set; }
    
    [JsonProperty("SelectedEntityFields")]
    public string? SelectedEntityFields { get; set; }
}