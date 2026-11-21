namespace DTO;

/// <summary>Names the Target Column that holds the Salesforce record ID.</summary>
public record SetKeyMappingDTO {
    public string TargetColumnName { get; set; } = "";
}
