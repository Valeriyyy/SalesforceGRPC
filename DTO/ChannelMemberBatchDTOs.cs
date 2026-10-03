namespace DTO;

/// <summary>
/// Request to add several Entities to a Channel in one submission. All are added or none are.
/// </summary>
public record AddChannelMembersDTO {
    public List<CreateChannelMemberDTO> Members { get; set; } = [];
}

/// <summary>
/// What became of an all-or-nothing submission of Channel Members.
/// </summary>
public record AddChannelMembersResultDTO {
    /// <summary>True when every Entity was added; false when none were.</summary>
    public bool Added { get; set; }

    /// <summary>One entry per submitted Entity, in submission order.</summary>
    public List<ChannelMemberOutcomeDTO> Outcomes { get; set; } = [];

    /// <summary>
    /// Members Salesforce created that could not be removed again after the submission failed, so they now
    /// exist in Salesforce though not here. Empty unless the rollback itself failed; a Resync picks them up.
    /// </summary>
    public List<string> LeftInSalesforce { get; set; } = [];
}

/// <summary>What happened to one Entity of an all-or-nothing submission.</summary>
public record ChannelMemberOutcomeDTO {
    public string SelectedEntity { get; set; } = "";

    /// <summary>"Added", "Failed" (Salesforce rejected this Entity) or "NotAdded" (another Entity failed).</summary>
    public string Status { get; set; } = "";

    /// <summary>Salesforce's reason, for a Failed Entity only.</summary>
    public string? Message { get; set; }

    /// <summary>The mirrored member's id, for an Added Entity only.</summary>
    public int? MemberId { get; set; }
}
