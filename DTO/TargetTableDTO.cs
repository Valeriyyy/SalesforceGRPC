namespace DTO;

/// <summary>A table in the Target Database that a Binding could write to.</summary>
public record TargetTableDTO {
    /// <summary>Null for an engine with no schema concept (e.g. SQLite).</summary>
    public string? SchemaName { get; set; }
    public string TableName { get; set; } = "";

    /// <summary>The schema-qualified name, which is what a Binding stores.</summary>
    public string FullName { get; set; } = "";

    /// <summary>The Entity already bound to this table, or null when it is free.</summary>
    public string? BoundEntityName { get; set; }
}
