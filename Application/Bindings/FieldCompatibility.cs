using Salesforce.Avro;

namespace Application.Bindings;

/// <summary>
/// How well one Entity field's value fits the Target Column it is mapped to.
/// </summary>
public enum CompatibilityLevel {
    /// <summary>The value fits without loss.</summary>
    Compatible = 0,

    /// <summary>The mapping works but something is lost or unusual. Does not block activation.</summary>
    Warning = 1,

    /// <summary>The mapping cannot succeed. Blocks activation.</summary>
    Error = 2
}

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
