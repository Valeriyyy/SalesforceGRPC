using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class EnrichedFieldCreateResponse {
    [JsonProperty("id")]
    public string? Id { get; set; }
    
    [JsonProperty("success")]
    public bool Success { get; set; }
    
    [JsonProperty("errors")]
    public List<ToolingError>? Errors { get; set; }
}