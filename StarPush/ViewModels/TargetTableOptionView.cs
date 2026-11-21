namespace StarPush.ViewModels;

/// <summary>A table a Binding could write to. <see cref="BoundEntity"/> holds it already when set.</summary>
public sealed record TargetTableOptionView(
    string? SchemaName,
    string TableName,
    string FullName,
    string? BoundEntity,
    bool NameMatches);
