namespace DTO;

/// <summary>
/// Request to add an event/entity to a Salesforce platform event channel.
/// </summary>
public record CreateChannelMemberDTO {
    /// <summary>
    /// The entity to stream, e.g. "AccountChangeEvent" for Change Data Capture or "Order_Event__e" for a
    /// platform event. Must be one of the values from the selectable-entities endpoint. Cannot be changed
    /// after create.
    /// </summary>
    public string SelectedEntity { get; set; } = string.Empty;

    /// <summary>
    /// Optional SOQL-subset expression (API 56.0+) applied by Salesforce before delivery,
    /// e.g. <c>City__c = 'San Francisco'</c>.
    /// </summary>
    public string? FilterExpression { get; set; }

    /// <summary>
    /// Optional field names (API 51.0+) always included in the payload, even when unchanged.
    /// </summary>
    public List<string>? EnrichedFields { get; set; }
}
