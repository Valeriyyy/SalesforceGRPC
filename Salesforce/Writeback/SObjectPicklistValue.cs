using Newtonsoft.Json;

namespace Salesforce.Writeback;

public sealed class SObjectPicklistValue {
    [JsonProperty("value")]
    public string Value { get; set; } = "";

    [JsonProperty("active")]
    public bool Active { get; set; }
}
