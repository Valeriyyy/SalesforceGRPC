namespace Database.Models;

/// <summary>
/// Something that interrupted syncing, or could have lost changes, kept for the user to see after the fact.
/// </summary>
/// <remarks>
/// One unbroken stretch of trouble, not each retry inside it: <see cref="Occurrences"/> counts those.
/// <see cref="EndedAt"/> is null while it is still going on. Kept for 24 hours.
/// </remarks>
public class Incident {
    public int Id { get; set; }

    /// <summary>The direction it interrupted; null for the application starting or stopping.</summary>
    public RunDirection? Direction { get; set; }

    public IncidentKind Kind { get; set; }
    public required string Message { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int Occurrences { get; set; } = 1;
}
