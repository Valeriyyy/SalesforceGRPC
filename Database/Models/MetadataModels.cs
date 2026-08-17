namespace Database.Models;

/// <summary>
/// Represents metadata for a database table including columns and constraints.
/// </summary>
public class TableMetadata {
    /// <summary>
    /// The schema name the table belongs to (e.g., 'public').
    /// </summary>
    public required string SchemaName { get; set; }

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

    /// <summary>True when a PRIMARY KEY or UNIQUE constraint covers this column.</summary>
    public bool IsUnique => ColumnConstraints.Any(c =>
        c.ConstraintType.Contains("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
        c.ConstraintType.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Constraints specific to this column (e.g., PK, FK, UNIQUE).
    /// </summary>
    public List<ColumnConstraint> ColumnConstraints { get; set; } = [];
}

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
