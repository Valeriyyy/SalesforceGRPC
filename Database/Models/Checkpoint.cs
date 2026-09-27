namespace Database.Models;

/// <summary>
/// A row in salesforce.channel_checkpoints: the position in one Channel's event stream up to which every
/// event has been applied or deliberately skipped.
/// </summary>
/// <remarks>
/// The replay ID is opaque. It is stored and handed back to Salesforce exactly as received, and never parsed,
/// compared or turned into a number — Salesforce makes no promise that replay IDs order.
/// </remarks>
public sealed class Checkpoint {
    /// <summary>The local platform_event_channels.id this position belongs to.</summary>
    public int ChannelId { get; set; }

    /// <summary>The raw replay ID bytes Salesforce sent.</summary>
    public required byte[] ReplayId { get; set; }

    /// <summary>When it was saved, in UTC. Salesforce keeps events for 72 hours, so this says whether it can still be resumed from.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>How long Salesforce keeps change events, and so how long a Checkpoint can be resumed from.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(72);

    /// <summary>Whether Salesforce will have discarded the events after this position by <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => Age(now) > Retention;

    /// <summary>How long ago it was saved.</summary>
    public TimeSpan Age(DateTimeOffset now) => now - new DateTimeOffset(DateTime.SpecifyKind(SavedAt, DateTimeKind.Utc));

    /// <summary>How long until Salesforce discards the events after this position; zero once it has.</summary>
    public TimeSpan ExpiresIn(DateTimeOffset now) => Age(now) >= Retention ? TimeSpan.Zero : Retention - Age(now);
}
