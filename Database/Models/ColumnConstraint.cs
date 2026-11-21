namespace Database.Models;

/// <summary>
/// Represents a constraint that applies to a specific column.
/// </summary>
public class ColumnConstraint {
    /// <summary>
    /// The constraint type (PRIMARY KEY, FOREIGN KEY, UNIQUE, CHECK, NOT NULL).
    /// </summary>
    public required string ConstraintType { get; set; }

    /// <summary>
    /// The name of the constraint.
    /// </summary>
    public string? ConstraintName { get; set; }

    /// <summary>
    /// For FOREIGN KEY constraints, the referenced table.
    /// </summary>
    public string? ReferencedTable { get; set; }

    /// <summary>
    /// For FOREIGN KEY constraints, the referenced column.
    /// </summary>
    public string? ReferencedColumn { get; set; }
}
