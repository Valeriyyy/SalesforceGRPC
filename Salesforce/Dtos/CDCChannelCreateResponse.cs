using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class CDCChannelCreateResponse {
    [JsonProperty("id")]
    public string? Id { get; set; }
    
    [JsonProperty("success")]
    public bool Success { get; set; }
    
    [JsonProperty("errors")]
    public List<ToolingError>? Errors { get; set; }
}