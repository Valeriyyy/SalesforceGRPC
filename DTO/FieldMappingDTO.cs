namespace DTO;

/// <summary>One Field Mapping: a flattened Salesforce field name paired with a Target Column name.</summary>
public record FieldMappingDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string TargetColumnName { get; set; } = "";
}
