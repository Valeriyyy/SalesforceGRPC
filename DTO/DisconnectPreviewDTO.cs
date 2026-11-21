namespace DTO;

/// <summary>What a Disconnect would destroy, so the user can be asked before it happens.</summary>
public record DisconnectPreviewDTO {
    public int Bindings { get; set; }
    public int FieldMappings { get; set; }
    public int AvroSchemas { get; set; }
    public int Channels { get; set; }
    public int ChannelMembers { get; set; }

    /// <summary>
    /// The Target Connection that will be destroyed too, as engine and address, or null when none is set up.
    /// </summary>
    /// <remarks>
    /// Where data lands is orthogonal to which org it comes from, so this is the one piece of state a user
    /// might not expect a Salesforce Disconnect to take. It is named here so it is disclosed, not discovered.
    /// </remarks>
    public string? TargetConnection { get; set; }

    /// <summary>
    /// What is left standing inside Salesforce, for the user to remove themselves if they want to.
    /// </summary>
    /// <remarks>
    /// Deleting objects in a customer's production org is an outward, hard-to-reverse action, and a partial
    /// remote cleanup during a local wipe leaves a state neither side can describe. An orphaned External
    /// Client App is harmless and auditable; a half-deleted one is not.
    /// </remarks>
    public List<string> LeftInSalesforce { get; set; } = [];
}
