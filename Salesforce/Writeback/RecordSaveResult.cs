using Newtonsoft.Json;

namespace Salesforce.Writeback;

/// <summary>What Salesforce said about one record of a collection call.</summary>
public sealed class RecordSaveResult {
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>For an upsert, whether the record was created rather than matched.</summary>
    [JsonProperty("created")]
    public bool Created { get; set; }

    [JsonProperty("errors")]
    public List<RecordError> Errors { get; set; } = [];
}
