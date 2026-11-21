namespace Database.Models;

/// <summary>
/// Represents metadata for a database column.
/// </summary>
public class ColumnMetadata {
    /// <summary>
    /// The name of the column.
    /// </summary>
    public required string ColumnName { get; set; }

    /// <summary>
    /// The PostgreSQL data type (e.g., 'integer', 'text', 'timestamp').
    /// </summary>
    public required string DataType { get; set; }

    /// <summary>
    /// Whether the column allows NULL values.
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// The default value as a string, or null if no default is defined.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// The ordinal position of the column in the table (1-based).
    /// </summary>
    public int OrdinalPosition { get; set; }

    /// <summary>
    /// The declared maximum length for character types, or null when the type is unbounded or not textual.
    /// Type Compatibility uses this to warn that a Salesforce value could be truncated.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>The total number of significant digits for numeric types, or null.</summary>
    public int? NumericPrecision { get; set; }

    /// <summary>The number of digits after the decimal point for numeric types, or null.</summary>
    public int? NumericScale { get; set; }

    /// <summary>
    /// True when the database writes the column itself and refuses a value for it: a generated column, or an
    /// identity declared GENERATED ALWAYS. Such a column can be read for Writeback but never written inbound.
    /// </summary>
    public bool IsGenerated { get; set; }

    /// <summary>
    /// True when the database limits the column to a set of values: a CHECK constraint names it, or its type is
    /// an enum. Writeback lets such a column feed a restricted Salesforce picklist.
    /// </summary>
    public bool IsCheckConstrained { get; set; }

    /// <summary>True when a PRIMARY KEY constraint covers this column.</summary>
    public bool IsPrimaryKey => ColumnConstraints.Any(c =>
        c.ConstraintType.Contains("PRIMARY", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when a PRIMARY KEY or UNIQUE constraint covers this column.</summary>
    public bool IsUnique => ColumnConstraints.Any(c =>
        c.ConstraintType.Contains("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
        c.ConstraintType.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Constraints specific to this column (e.g., PK, FK, UNIQUE).
    /// </summary>
    public List<ColumnConstraint> ColumnConstraints { get; set; } = [];
}
