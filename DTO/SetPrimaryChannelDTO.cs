namespace DTO;

/// <summary>Selects the Primary Channel — the single channel the worker subscribes to.</summary>
public record SetPrimaryChannelDTO {
    public int ChannelId { get; set; }

    /// <summary>
    /// Where to begin when the channel still has a Checkpoint: Resume (the default), Earliest or Latest.
    /// Ignored when it has none, and Resume is refused once the Checkpoint is older than 72 hours.
    /// </summary>
    public string? Start { get; set; }
}
