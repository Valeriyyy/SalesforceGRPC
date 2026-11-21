namespace Database.Models;

/// <summary>
/// Represents a constraint applied to a database table or column.
/// </summary>
public class ConstraintMetadata {
    /// <summary>
    /// The name of the constraint.
    /// </summary>
    public required string ConstraintName { get; set; }

    /// <summary>
    /// The type of constraint (PRIMARY KEY, FOREIGN KEY, UNIQUE, CHECK, NOT NULL).
    /// </summary>
    public required string ConstraintType { get; set; }

    /// <summary>
    /// The columns involved in the constraint.
    /// </summary>
    public required List<string> Columns { get; set; } = [];

    /// <summary>
    /// For FOREIGN KEY constraints, the referenced table name.
    /// </summary>
    public string? ReferencedTableName { get; set; }

    /// <summary>
    /// For FOREIGN KEY constraints, the referenced column names.
    /// </summary>
    public List<string> ReferencedColumns { get; set; } = [];

    /// <summary>
    /// The definition of the constraint (e.g., for CHECK constraints).
    /// </summary>
    public string? ConstraintDefinition { get; set; }
}
