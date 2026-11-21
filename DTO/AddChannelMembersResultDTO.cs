namespace DTO;

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
