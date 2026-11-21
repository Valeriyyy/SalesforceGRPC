namespace DTO;

public record WritebackDefaultDTO {
    /// <summary>The Salesforce field as the REST API names it.</summary>
    public string SalesforceFieldName { get; set; } = "";
    public string Value { get; set; } = "";
}
