namespace DTO;

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
