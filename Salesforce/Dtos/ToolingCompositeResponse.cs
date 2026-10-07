using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Salesforce.Dtos;

/// <summary>
/// The answer to a Tooling API <c>composite</c> request: one subresponse per subrequest, in order.
/// </summary>
public class ToolingCompositeResponse {
    [JsonProperty("compositeResponse")]
    public List<ToolingCompositeSubresponse> CompositeResponse { get; set; } = [];
}

/// <summary>
/// One subrequest's result. <see cref="Body"/> is a save result on success and an error array on failure.
/// </summary>
public class ToolingCompositeSubresponse {
    [JsonProperty("referenceId")]
    public string? ReferenceId { get; set; }

    [JsonProperty("httpStatusCode")]
    public int HttpStatusCode { get; set; }

    [JsonProperty("body")]
    public JToken? Body { get; set; }

    [JsonIgnore]
    public bool IsSuccess => HttpStatusCode is >= 200 and < 300;

    /// <summary>The created record's id, when this subrequest created one.</summary>
    [JsonIgnore]
    public string? CreatedId => IsSuccess && Body is JObject saved ? saved.Value<string>("id") : null;

    /// <summary>Salesforce's errors for a failed subrequest; empty on success.</summary>
    [JsonIgnore]
    public IReadOnlyList<ToolingError> Errors => IsSuccess || Body is null
        ? []
        : Body.Type == JTokenType.Array
            ? Body.ToObject<List<ToolingError>>() ?? []
            : Body.ToObject<ToolingError>() is { } single ? [single] : [];
}
