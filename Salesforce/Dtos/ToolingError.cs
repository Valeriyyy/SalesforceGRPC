using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class ToolingError {
    [JsonProperty("message")]
    public string? Message { get; set; }
    
    [JsonProperty("errorCode")]
    public string? ErrorCode { get; set; }
}