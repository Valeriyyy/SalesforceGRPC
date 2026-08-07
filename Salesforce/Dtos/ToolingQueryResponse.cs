using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class ToolingQueryResponse<T> {
    [JsonProperty("totalSize")]
    public int TotalSize { get; set; }
    
    [JsonProperty("done")]
    public bool Done { get; set; }
    
    [JsonProperty("records")]
    public List<T>? Records { get; set; }
}