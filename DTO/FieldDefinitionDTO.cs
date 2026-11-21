namespace DTO;

public record FieldDefinitionDTO {
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>"String", "Int", "Bool", "Secret" or "Choice".</summary>
    public string Kind { get; set; } = "";
    public bool Required { get; set; }
    public string? Default { get; set; }

    /// <summary>The allowed values, for a Choice. Null otherwise.</summary>
    public List<string>? Choices { get; set; }
}
