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

/// <summary>
/// Request to create a Change Data Capture Channel from the Channels page: always <c>data</c>, with the
/// Starting Point it begins from and, optionally, made the Primary Channel in the same step.
/// </summary>
public record NewChannelDTO {
    /// <summary>The channel full name including the <c>__chn</c> suffix, e.g. "SalesEvents__chn".</summary>
    public string FullName { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>"Latest" (the default) or "Earliest".</summary>
    public string? StartingPoint { get; set; }

    public bool MakePrimary { get; set; }
}

/// <summary>The Channel a <see cref="NewChannelDTO"/> produced.</summary>
public record NewChannelResultDTO {
    public int ChannelId { get; set; }

    /// <summary>
    /// True when Salesforce already had a Channel with this Full Name, so it was adopted rather than created.
    /// </summary>
    public bool Adopted { get; set; }

    /// <summary>How many Channel Members it carries — always 0 for a Channel that was created.</summary>
    public int MemberCount { get; set; }

    public string FullName { get; set; } = string.Empty;
}
