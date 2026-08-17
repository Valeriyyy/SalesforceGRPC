namespace DTO;

/// <summary>
/// Creates a Binding for a Channel Member's Entity, pointing it at an existing Target Table.
/// </summary>
/// <remarks>
/// Schema and table are separate rather than one qualified string because the Target Database metadata
/// queries take them separately, and splitting a user-supplied "a.b.c" is guesswork.
/// </remarks>
public record CreateBindingDTO {
    /// <summary>The schema containing the Target Table, e.g. "salesforce".</summary>
    public string TargetSchema { get; set; } = "public";

    /// <summary>The Target Table name, e.g. "account".</summary>
    public string TargetTable { get; set; } = "";
}

/// <summary>One Field Mapping: a flattened Salesforce field name paired with a Target Column name.</summary>
public record FieldMappingDTO {
    public string SalesforceFieldName { get; set; } = "";
    public string TargetColumnName { get; set; } = "";
}

/// <summary>
/// Replaces a Binding's entire Field Mapping set.
/// </summary>
/// <remarks>
/// Replace rather than append: the caller always holds the full intended set, so a merge would leave stored
/// mappings matching neither what the user saw nor what they submitted. The Key Mapping is not part of this
/// set and is unaffected.
/// </remarks>
public record SetFieldMappingsDTO {
    public List<FieldMappingDTO> Mappings { get; set; } = [];
}

/// <summary>Names the Target Column that holds the Salesforce record ID.</summary>
public record SetKeyMappingDTO {
    public string TargetColumnName { get; set; } = "";
}

/// <summary>Turns soft delete on or off for a Binding and names the column carrying the flag.</summary>
public record SetSoftDeleteDTO {
    public bool Enabled { get; set; }

    /// <summary>Required when <see cref="Enabled"/> is true; ignored otherwise.</summary>
    public string? ColumnName { get; set; }
}

/// <summary>Selects the Primary Channel — the single channel the worker subscribes to.</summary>
public record SetPrimaryChannelDTO {
    public int ChannelId { get; set; }
}
