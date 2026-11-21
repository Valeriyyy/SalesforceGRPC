namespace DTO;

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
