using Newtonsoft.Json;

namespace Salesforce.Writeback;

public sealed class RecordError {
    [JsonProperty("statusCode")]
    public string StatusCode { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    [JsonProperty("fields")]
    public List<string> Fields { get; set; } = [];

    /// <summary>
    /// The status codes that say "not now" rather than "no": the same record may succeed if sent again.
    /// </summary>
    public static readonly IReadOnlySet<string> TransientStatusCodes = new HashSet<string>(StringComparer.Ordinal) {
        "UNABLE_TO_LOCK_ROW", "REQUEST_LIMIT_EXCEEDED", "SERVER_UNAVAILABLE", "REQUEST_RUNNING_TOO_LONG"
    };

    [JsonIgnore]
    public bool IsTransient => TransientStatusCodes.Contains(StatusCode);
}
