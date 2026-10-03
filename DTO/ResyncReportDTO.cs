namespace DTO;

/// <summary>
/// What a Resync found changed in Salesforce, and what it did about it. Only Change Data Capture Channels are
/// reported; platform event channels are mirrored but never shown.
/// </summary>
public record ResyncReportDTO {
    /// <summary>Full Names of Channels that exist in Salesforce but were not mirrored.</summary>
    public List<string> ChannelsAdded { get; set; } = [];

    /// <summary>Full Names of mirrored Channels Salesforce no longer has.</summary>
    public List<string> ChannelsRemoved { get; set; } = [];

    /// <summary>Full Names of Channels whose Label changed in Salesforce.</summary>
    public List<string> ChannelsRelabelled { get; set; } = [];

    /// <summary>Entities newly carried by a Channel that was already mirrored.</summary>
    public List<ResyncMemberChangeDTO> MembersAdded { get; set; } = [];

    /// <summary>Entities a Channel that is still mirrored no longer carries.</summary>
    public List<ResyncMemberChangeDTO> MembersRemoved { get; set; } = [];

    /// <summary>
    /// The Full Name of the Primary Channel when Salesforce no longer has it, so nothing is streaming; otherwise
    /// null.
    /// </summary>
    public string? PrimaryChannelRemoved { get; set; }

    public bool HasChanges => ChannelsAdded.Count > 0 || ChannelsRemoved.Count > 0 || ChannelsRelabelled.Count > 0
                              || MembersAdded.Count > 0 || MembersRemoved.Count > 0;
}

/// <summary>One Entity added to or removed from a Channel by a Resync.</summary>
public record ResyncMemberChangeDTO {
    public string Channel { get; set; } = "";
    public string SelectedEntity { get; set; } = "";

    /// <summary>
    /// The Target Table of the Binding this removal set Inactive, when it was removed from the Primary Channel and
    /// its Binding was Active; otherwise null.
    /// </summary>
    public string? BindingSetInactive { get; set; }
}
