namespace DTO;

/// <summary>
/// Request to create a Salesforce platform event channel.
/// </summary>
public record CreateChannelDTO {
    /// <summary>
    /// The channel full name including the <c>__chn</c> suffix, e.g. "SalesEvents__chn".
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>The display label shown in Salesforce Setup.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// "data" for Change Data Capture or "event" for platform events. Cannot be changed after create.
    /// </summary>
    public string ChannelType { get; set; } = "data";

    /// <summary>
    /// Optional (API 61.0+): "custom", "data" or "monitoring". Cannot be changed after create.
    /// </summary>
    public string? EventType { get; set; }
}
