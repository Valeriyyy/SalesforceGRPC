namespace DTO;

/// <summary>
/// One field of an Entity that a Field Mapping may name.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is already flattened — a compound Salesforce field appears as its parts
/// ("BillingAddressCity"), never under its own name, because a compound has no single value to write.
/// </remarks>
public record BindableFieldDTO {
    public string Name { get; set; } = "";

    /// <summary>The semantic Salesforce type, e.g. "DateOnly", "Currency", "EntityId".</summary>
    public string FieldType { get; set; } = "";

    /// <summary>The Avro wire type the value arrives as. Never enough on its own to judge a mapping by.</summary>
    public string AvroType { get; set; } = "";

    public bool IsNullable { get; set; }

    /// <summary>The compound parent this field was flattened out of, or null for a top-level field.</summary>
    public string? ParentName { get; set; }

    /// <summary>The Target Column this field is currently mapped to, or null when unmapped.</summary>
    public string? MappedColumnName { get; set; }

    /// <summary>
    /// A Target Column whose name matches this field once naming conventions are normalised, offered so the
    /// common case is a review rather than data entry. Null when nothing matches.
    /// </summary>
    public string? SuggestedColumnName { get; set; }
}
