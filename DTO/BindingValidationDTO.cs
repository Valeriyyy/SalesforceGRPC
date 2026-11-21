namespace DTO;

/// <summary>
/// Everything validation found, reported field by field so the user knows exactly what to change.
/// </summary>
public record BindingValidationDTO {
    public int BindingId { get; set; }

    /// <summary>True when the Binding may be activated — no blockers and no Error-level results.</summary>
    public bool CanActivate { get; set; }

    /// <summary>
    /// Problems that are not about one field: no Key Mapping, no Field Mappings, the Target Table is gone.
    /// </summary>
    public List<string> Blockers { get; set; } = [];

    public List<CompatibilityResultDTO> Results { get; set; } = [];

    /// <summary>
    /// The Salesforce Schema Id validation ran against, so a later Salesforce change that invalidates this
    /// result is recognisable.
    /// </summary>
    public string? ValidatedAgainstSchemaId { get; set; }
}
