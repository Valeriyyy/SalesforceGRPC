namespace DTO;

/// <summary>
/// Replaces a Binding's entire Field Mapping set.
/// </summary>
/// <remarks>
/// Replace rather than append: the caller always holds the full intended set, so a merge would leave stored
/// mappings matching neither what the user saw nor what they submitted. The Key Mapping is not part of this
/// set and is unaffected.
/// </remarks>
public record SetFieldMappingsDTO {
    public List<FieldMappingDTO> Mappings { get; set; } = [];
}
