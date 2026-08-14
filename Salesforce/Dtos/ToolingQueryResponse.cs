using Newtonsoft.Json;

namespace Salesforce.Dtos;

public class ToolingQueryResponse<T> {
    [JsonProperty("totalSize")]
    public int TotalSize { get; set; }
    [JsonProperty("size")]
    public int Size { get; set; }
    [JsonProperty("done")]
    public bool Done { get; set; }
    [JsonProperty("nextRecordsUrl")]
    public string? NextRecordsUrl { get; set; }
    [JsonProperty("queryLocator")]
    public string? QueryLocator { get; set; }
    [JsonProperty("entityTypeName")]
    public string? EntityTypeName { get; set; }
    [JsonProperty("records")]
    public List<T>? Records { get; set; }
    [JsonProperty("value")]
    public T? Value { get; set; }
}
