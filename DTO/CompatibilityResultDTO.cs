namespace DTO;

/// <summary>The outcome of checking one mapped field, the Key Mapping, or the soft delete column.</summary>
public record CompatibilityResultDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string TargetColumnName { get; set; } = "";
    public string FieldType { get; set; } = "";
    public string TargetDataType { get; set; } = "";

    /// <summary>"Compatible", "Warning" or "Error". Only Error blocks activation.</summary>
    public string Level { get; set; } = "";

    public string Message { get; set; } = "";
}
