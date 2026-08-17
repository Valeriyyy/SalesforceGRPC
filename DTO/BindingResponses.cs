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

/// <summary>A table in the Target Database that a Binding could write to.</summary>
public record TargetTableDTO {
    public string SchemaName { get; set; } = "";
    public string TableName { get; set; } = "";

    /// <summary>The schema-qualified name, which is what a Binding stores.</summary>
    public string FullName { get; set; } = "";

    /// <summary>The Entity already bound to this table, or null when it is free.</summary>
    public string? BoundEntityName { get; set; }
}

/// <summary>A column of a Target Table — the right-hand side of a Field Mapping.</summary>
public record TargetColumnDTO {
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }

    /// <summary>True when a PRIMARY KEY or UNIQUE constraint covers this column.</summary>
    public bool IsUnique { get; set; }

    /// <summary>The Salesforce field currently mapped here, or null when nothing writes to it.</summary>
    public string? MappedSalesforceFieldName { get; set; }
}

/// <summary>A Binding, as the API reports it.</summary>
public record BindingDTO {
    public int Id { get; set; }
    public string EntityName { get; set; } = "";

    /// <summary>The schema-qualified Target Table, e.g. "salesforce.account".</summary>
    public string TargetTable { get; set; } = "";

    /// <summary>"Incomplete", "Active" or "Inactive".</summary>
    public string State { get; set; } = "";

    /// <summary>The Target Column holding the Salesforce record ID, or null when not chosen yet.</summary>
    public string? KeyMappingColumn { get; set; }

    /// <summary>Field Mappings excluding the Key Mapping.</summary>
    public int FieldMappingCount { get; set; }

    public bool SoftDeleteEnabled { get; set; }
    public string? SoftDeleteColumnName { get; set; }

    /// <summary>The Salesforce Schema Id of the Avro Schema this Binding was last linked to.</summary>
    public string? AvroSchemaId { get; set; }

    /// <summary>Local IDs of the Channel Members pointing at this Binding. Empty when orphaned.</summary>
    public List<int> ChannelMemberIds { get; set; } = [];
}

/// <summary>The outcome of checking one mapped field, the Key Mapping, or the soft delete column.</summary>
public record CompatibilityResultDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string TargetColumnName { get; set; } = "";
    public string FieldType { get; set; } = "";
    public string TargetDataType { get; set; } = "";

    /// <summary>"Compatible", "Warning" or "Error". Only Error blocks activation.</summary>
    public string Level { get; set; } = "";

    public string Message { get; set; } = "";
}

/// <summary>
/// Everything validation found, reported field by field so the user knows exactly what to change.
/// </summary>
public record BindingValidationDTO {
    public int BindingId { get; set; }

    /// <summary>True when the Binding may be activated — no blockers and no Error-level results.</summary>
    public bool CanActivate { get; set; }

    /// <summary>
    /// Problems that are not about one field: no Key Mapping, no Field Mappings, the Target Table is gone.
    /// </summary>
    public List<string> Blockers { get; set; } = [];

    public List<CompatibilityResultDTO> Results { get; set; } = [];

    /// <summary>
    /// The Salesforce Schema Id validation ran against, so a later Salesforce change that invalidates this
    /// result is recognisable.
    /// </summary>
    public string? ValidatedAgainstSchemaId { get; set; }
}
