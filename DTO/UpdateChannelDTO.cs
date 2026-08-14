namespace DTO;

/// <summary>
/// Request to update a Salesforce platform event channel. Only the label is mutable — Salesforce fixes
/// ChannelType and EventType at create time.
/// </summary>
public record UpdateChannelDTO {
    /// <summary>The new display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Optional. If supplied it must match the channel's existing value; the request is rejected otherwise
    /// rather than silently ignored, because Salesforce cannot change it.
    /// </summary>
    public string? ChannelType { get; set; }

    /// <summary>
    /// Optional. If supplied it must match the channel's existing value, for the same reason.
    /// </summary>
    public string? EventType { get; set; }
}
