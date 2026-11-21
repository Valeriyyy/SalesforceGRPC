using Salesforce.Avro;

namespace Application.Bindings;

/// <summary>
/// The result of checking one Field Mapping, or of checking the Key Mapping or soft delete column.
/// </summary>
/// <remarks>
/// Both type names are carried so the user is told what to change rather than just that something is wrong.
/// </remarks>
public sealed record FieldCompatibility {
    public required string SalesforceFieldName { get; init; }
    public required string TargetColumnName { get; init; }
    public required SalesforceFieldType FieldType { get; init; }
    public required string TargetDataType { get; init; }
    public required CompatibilityLevel Level { get; init; }
    public required string Message { get; init; }

    public override string ToString() => $"{Level}: {Message}";
}
