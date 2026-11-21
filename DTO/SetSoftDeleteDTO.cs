namespace DTO;

/// <summary>Turns soft delete on or off for a Binding and names the column carrying the flag.</summary>
public record SetSoftDeleteDTO {
    public bool Enabled { get; set; }

    /// <summary>Required when <see cref="Enabled"/> is true; ignored otherwise.</summary>
    public string? ColumnName { get; set; }
}
