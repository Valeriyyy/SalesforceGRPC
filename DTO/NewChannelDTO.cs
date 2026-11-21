namespace DTO;

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
