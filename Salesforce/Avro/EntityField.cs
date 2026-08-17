namespace Salesforce.Avro;

/// <summary>
/// One bindable field of an Entity — the left-hand side of a Field Mapping.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is already flattened, so it is exactly the string that must appear in mapped_fields for
/// the worker's strategies to find it.
/// </remarks>
public sealed record EntityField {
    /// <summary>The flattened Salesforce field name, e.g. "Phone" or "BillingAddressCity".</summary>
    public required string Name { get; init; }

    public required SalesforceFieldType FieldType { get; init; }

    /// <summary>The Avro wire type the value arrives as — "string", "long", "double", "int", "boolean".</summary>
    public required string AvroType { get; init; }

    /// <summary>True when the Avro union includes null, which every Salesforce data field does.</summary>
    public bool IsNullable { get; init; }

    /// <summary>The compound parent this field was flattened out of, or null for a top-level field.</summary>
    public string? ParentName { get; init; }

    /// <summary>The child name within the compound parent, or null for a top-level field.</summary>
    public string? ChildName { get; init; }

    /// <summary>The Salesforce field ID from the doc annotation, present only on custom fields.</summary>
    public string? FieldId { get; init; }

    public override string ToString() => $"{Name} ({FieldType})";
}
