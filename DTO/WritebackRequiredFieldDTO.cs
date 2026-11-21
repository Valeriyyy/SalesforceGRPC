namespace DTO;

/// <summary>A Salesforce field a record cannot be created without.</summary>
public record WritebackRequiredFieldDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string? Label { get; set; }
    public string Type { get; set; } = "";

    /// <summary>True when a Field Mapping that carries Writeback supplies it.</summary>
    public bool SuppliedByMapping { get; set; }

    public string? DefaultValue { get; set; }

    /// <summary>Picklist values Salesforce accepts, so the editor can offer a real one as the default.</summary>
    public List<string> AllowedValues { get; set; } = [];
}
