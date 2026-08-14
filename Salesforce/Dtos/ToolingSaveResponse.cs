using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// The response shape returned by Tooling API create/update calls.
/// </summary>
public class ToolingSaveResponse {
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("errors")]
    public List<ToolingError>? Errors { get; set; }
}
