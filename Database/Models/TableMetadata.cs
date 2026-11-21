namespace Database.Models;

/// <summary>
/// Represents metadata for a database table including columns and constraints.
/// </summary>
public class TableMetadata {
    /// <summary>
    /// The schema name the table belongs to (e.g., 'public'), or null for an engine with no
    /// schema concept (e.g. SQLite).
    /// </summary>
    public required string? SchemaName { get; set; }

    /// <summary>
    /// The name of the table.
    /// </summary>
    public required string TableName { get; set; }

    /// <summary>
    /// The collection of columns in the table.
    /// </summary>
    public required List<ColumnMetadata> Columns { get; set; } = [];

    /// <summary>
    /// All constraints defined on this table.
    /// </summary>
    public required List<ConstraintMetadata> Constraints { get; set; } = [];
}
