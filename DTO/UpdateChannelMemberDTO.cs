namespace DTO;

/// <summary>
/// Request to update a channel member. Only the filter expression and enriched fields are mutable —
/// Salesforce fixes the channel and the selected entity at create time.
/// </summary>
/// <remarks>
/// Both fields replace rather than merge: passing null or an empty list for <see cref="EnrichedFields"/>
/// clears the existing set, matching how Salesforce treats a PATCH.
/// </remarks>
public record UpdateChannelMemberDTO {
    public string? FilterExpression { get; set; }

    public List<string>? EnrichedFields { get; set; }

    /// <summary>
    /// Optional. If supplied it must match the member's existing entity; the request is rejected
    /// otherwise rather than silently ignored, because Salesforce cannot change it.
    /// </summary>
    public string? SelectedEntity { get; set; }
}
