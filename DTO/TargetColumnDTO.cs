namespace DTO;

/// <summary>A column of a Target Table — the right-hand side of a Field Mapping.</summary>
public record TargetColumnDTO {
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }

    /// <summary>True when a PRIMARY KEY or UNIQUE constraint covers this column.</summary>
    public bool IsUnique { get; set; }

    /// <summary>True when a PRIMARY KEY constraint covers this column: the table's own key, not a home for the record ID.</summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>The Salesforce field currently mapped here, or null when nothing writes to it.</summary>
    public string? MappedSalesforceFieldName { get; set; }
}
