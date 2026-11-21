namespace DTO;

/// <summary>
/// Everything the Binding editor needs to offer Two-way: whether it can be chosen, the Writeback Key choices,
/// each Field Mapping's effective Field Direction, the required Salesforce fields, and what stands in the way.
/// </summary>
public record WritebackSetupDTO {
    public int BindingId { get; set; }

    /// <summary>Inbound or TwoWay, as stored.</summary>
    public string SyncDirection { get; set; } = "";

    /// <summary>False when Two-way cannot be chosen at all: the engine does not support it, or the server is too old.</summary>
    public bool CanBeTwoWay { get; set; }

    public string? UnavailableReason { get; set; }

    public string? WritebackKeyColumn { get; set; }
    public string? WritebackKeySalesforceField { get; set; }

    /// <summary>Target Columns that can identify a row: unique, and not the Key Mapping column.</summary>
    public List<string> KeyColumnCandidates { get; set; } = [];

    /// <summary>Salesforce fields that can store the Writeback Key: External ID and Unique.</summary>
    public List<string> ExternalIdFields { get; set; } = [];

    public List<WritebackFieldDTO> Fields { get; set; } = [];

    public List<WritebackRequiredFieldDTO> RequiredFields { get; set; } = [];

    /// <summary>What would refuse Two-way as the Binding is now configured, each with the SQL that fixes it where there is one.</summary>
    public List<WritebackIssueDTO> Issues { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    /// <summary>Open Writeback Failures for this Binding.</summary>
    public int FailureCount { get; set; }
}
