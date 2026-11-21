namespace Database.Repositories.Interfaces;

/// <summary>What a repoint destroys, so the user can be told before they lose it.</summary>
public sealed record BindingCounts {
    public int Bindings { get; init; }
    public int FieldMappings { get; init; }
}
