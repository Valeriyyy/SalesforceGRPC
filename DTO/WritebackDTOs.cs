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

/// <summary>One Field Mapping's direction within a Two-way Binding.</summary>
public record WritebackFieldDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string TargetColumnName { get; set; } = "";

    /// <summary>The Salesforce field as the REST API names it (FirstName for NameFirstName), or null when Salesforce does not report it.</summary>
    public string? RestFieldName { get; set; }

    /// <summary>The direction in effect: TwoWay, InboundOnly or WritebackOnly.</summary>
    public string Direction { get; set; } = "";

    /// <summary>The user's own choice, kept even while a forced direction outranks it.</summary>
    public string ChosenDirection { get; set; } = "";

    /// <summary>True when what one side accepts decides the direction and the user cannot change it.</summary>
    public bool Forced { get; set; }

    public string? Reason { get; set; }
}

/// <summary>A Salesforce field a record cannot be created without.</summary>
public record WritebackRequiredFieldDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string? Label { get; set; }
    public string Type { get; set; } = "";

    /// <summary>True when a Field Mapping that carries Writeback supplies it.</summary>
    public bool SuppliedByMapping { get; set; }

    public string? DefaultValue { get; set; }

    /// <summary>Picklist values Salesforce accepts, so the editor can offer a real one as the default.</summary>
    public List<string> AllowedValues { get; set; } = [];
}

/// <summary>Something that refuses Two-way, and the SQL that fixes it when it is a database prerequisite.</summary>
public record WritebackIssueDTO {
    public string Message { get; set; } = "";
    public string? Sql { get; set; }
}

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

public record FieldDirectionDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string Direction { get; set; } = "";
}

public record WritebackDefaultDTO {
    /// <summary>The Salesforce field as the REST API names it.</summary>
    public string SalesforceFieldName { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>A Target Table change Salesforce refused.</summary>
public record WritebackFailureDTO {
    public int Id { get; set; }
    public int BindingId { get; set; }
    public string? WritebackKeyValue { get; set; }
    public string? SalesforceId { get; set; }
    public string Operation { get; set; } = "";
    public string StatusCodes { get; set; } = "";
    public string Message { get; set; } = "";
    public string ValuesSent { get; set; } = "{}";
    public DateTime FirstFailedAt { get; set; }
    public DateTime LastFailedAt { get; set; }
    public int Attempts { get; set; }
}

/// <summary>What became of a Retry.</summary>
public record WritebackRetryDTO {
    /// <summary>Succeeded, Failed, or Discarded (the row no longer exists).</summary>
    public string Outcome { get; set; } = "";
    public string? Message { get; set; }
}
