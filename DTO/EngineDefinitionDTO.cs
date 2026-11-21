namespace DTO;

/// <summary>
/// One Target Database Engine: whether it can be used, and which fields it asks for.
/// </summary>
/// <remarks>
/// This is what makes a setup form renderable from data. A fifth engine is a new entry here, not a new form.
/// Unavailable engines are listed with their reason rather than omitted, so a client can explain why an
/// option is greyed out.
/// </remarks>
public record EngineDefinitionDTO {
    public string Engine { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }

    /// <summary>Whether a Binding on this engine can be Two-way.</summary>
    public bool SupportsWriteback { get; set; }

    public List<FieldDefinitionDTO> Fields { get; set; } = [];
}
