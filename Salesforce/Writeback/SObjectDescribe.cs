using Newtonsoft.Json;

namespace Salesforce.Writeback;

/// <summary>The parts of a Source Object describe Writeback reads.</summary>
public sealed class SObjectDescribe {
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("fields")]
    public List<SObjectField> Fields { get; set; } = [];

    public SObjectField? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}
