namespace DTO;

/// <summary>Sets a Binding's Sync Direction, with the Writeback settings Two-way needs.</summary>
public record SetSyncDirectionDTO {
    /// <summary>Inbound or TwoWay.</summary>
    public string SyncDirection { get; set; } = "";

    public string? WritebackKeyColumn { get; set; }
    public string? WritebackKeySalesforceField { get; set; }

    /// <summary>The user's Field Directions. A field left out keeps the direction it has.</summary>
    public List<FieldDirectionDTO> FieldDirections { get; set; } = [];

    /// <summary>The Writeback Defaults, replacing every one the Binding has.</summary>
    public List<WritebackDefaultDTO> WritebackDefaults { get; set; } = [];
}
