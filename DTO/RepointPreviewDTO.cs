namespace DTO;

/// <summary>What a repoint would destroy, so the user can be asked before it happens.</summary>
public record RepointPreviewDTO {
    public int Bindings { get; set; }
    public int FieldMappings { get; set; }
}
