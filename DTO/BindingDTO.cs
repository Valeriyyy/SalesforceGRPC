namespace DTO;

/// <summary>A Binding, as the API reports it.</summary>
public record BindingDTO {
    public int Id { get; set; }
    public string EntityName { get; set; } = "";

    /// <summary>The schema-qualified Target Table, e.g. "salesforce.account".</summary>
    public string TargetTable { get; set; } = "";

    /// <summary>"Incomplete", "Active" or "Inactive".</summary>
    public string State { get; set; } = "";

    /// <summary>
    /// True when the Binding was Active and was forced back to Incomplete — by the worker or by an edit that
    /// broke it — rather than never finished. Its Entity's changes are not being synced.
    /// </summary>
    public bool NeedsAttention { get; set; }

    /// <summary>The Target Column holding the Salesforce record ID, or null when not chosen yet.</summary>
    public string? KeyMappingColumn { get; set; }

    /// <summary>Field Mappings excluding the Key Mapping.</summary>
    public int FieldMappingCount { get; set; }

    /// <summary>
    /// How many flattened fields the Entity carries in the Avro Schema this Binding was last linked to, or null
    /// when it has none. The "of 40" in "12 of 40 fields".
    /// </summary>
    public int? FieldCount { get; set; }

    public bool SoftDeleteEnabled { get; set; }
    public string? SoftDeleteColumnName { get; set; }

    /// <summary>The Salesforce Schema Id of the Avro Schema this Binding was last linked to.</summary>
    public string? AvroSchemaId { get; set; }

    /// <summary>Inbound or TwoWay.</summary>
    public string SyncDirection { get; set; } = "Inbound";

    /// <summary>Local IDs of the Channel Members pointing at this Binding. Empty when orphaned.</summary>
    public List<int> ChannelMemberIds { get; set; } = [];
}
