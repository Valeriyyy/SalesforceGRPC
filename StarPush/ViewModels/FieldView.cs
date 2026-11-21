using DTO;

namespace StarPush.ViewModels;

/// <summary>One detail an engine asks for. Typed fields and options alike; the name says which.</summary>
public sealed record FieldView(string Name, string Label, FieldKindView Kind, bool Required, string? Default,
    IReadOnlyList<string>? Choices) {

    public static FieldView From(FieldDefinitionDTO field) => new(
        field.Name,
        field.Label,
        Enum.Parse<FieldKindView>(field.Kind),
        field.Required,
        field.Default,
        field.Choices);
}
