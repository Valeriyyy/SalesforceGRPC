namespace DTO;

/// <summary>Where the worker would begin a channel: its Checkpoint, if any, and its Starting Point.</summary>
public record ChannelStartDTO {
    public int ChannelId { get; set; }

    /// <summary>Latest or Earliest — used only when there is no Checkpoint.</summary>
    public string StartingPoint { get; set; } = "";

    public bool HasCheckpoint { get; set; }

    /// <summary>When the Checkpoint was saved, in UTC, so its age can be shown. Null without one.</summary>
    public DateTime? CheckpointSavedAt { get; set; }

    /// <summary>
    /// Whether making this channel Primary can Resume from its Checkpoint. False without one, or once it is
    /// older than the 72 hours Salesforce keeps events for.
    /// </summary>
    public bool CanResume { get; set; }
}
