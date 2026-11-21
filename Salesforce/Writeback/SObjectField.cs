using Newtonsoft.Json;

namespace Salesforce.Writeback;

/// <summary>One field of a Source Object describe.</summary>
public sealed class SObjectField {
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("label")]
    public string? Label { get; set; }

    /// <summary>The describe type: string, double, currency, date, datetime, time, boolean, picklist, reference...</summary>
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("updateable")]
    public bool Updateable { get; set; }

    [JsonProperty("createable")]
    public bool Createable { get; set; }

    [JsonProperty("nillable")]
    public bool Nillable { get; set; }

    [JsonProperty("defaultedOnCreate")]
    public bool DefaultedOnCreate { get; set; }

    [JsonProperty("externalId")]
    public bool ExternalId { get; set; }

    [JsonProperty("unique")]
    public bool Unique { get; set; }

    [JsonProperty("calculated")]
    public bool Calculated { get; set; }

    [JsonProperty("length")]
    public int Length { get; set; }

    [JsonProperty("precision")]
    public int Precision { get; set; }

    [JsonProperty("scale")]
    public int Scale { get; set; }

    [JsonProperty("restrictedPicklist")]
    public bool RestrictedPicklist { get; set; }

    [JsonProperty("picklistValues")]
    public List<SObjectPicklistValue> PicklistValues { get; set; } = [];

    /// <summary>The compound field this one is part of (BillingAddress for BillingStreet), or null.</summary>
    [JsonProperty("compoundFieldName")]
    public string? CompoundFieldName { get; set; }

    /// <summary>True when Writeback may send a value for it at all.</summary>
    [JsonIgnore]
    public bool IsWritable => !Calculated && (Updateable || Createable);

    /// <summary>
    /// True when a record cannot be created without a value for it: Salesforce neither accepts null nor fills
    /// one in. A boolean always has a value, so it is never one of these.
    /// </summary>
    [JsonIgnore]
    public bool IsRequiredOnCreate => Createable && !Nillable && !DefaultedOnCreate && Type != "boolean";
}
