using Newtonsoft.Json;

namespace Salesforce.Dtos;

/// <summary>
/// The answer to a Tooling API <c>composite</c> request: one subresponse per subrequest, in order.
/// </summary>
public class ToolingCompositeResponse {
    [JsonProperty("compositeResponse")]
    public List<ToolingCompositeSubresponse> CompositeResponse { get; set; } = [];
}
