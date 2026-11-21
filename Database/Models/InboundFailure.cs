namespace Database.Models;

/// <summary>
/// A change event that could not be applied to the Target Database and was skipped, so the change is missing
/// there. Recorded for the user, never retried, and kept until the user dismisses it.
/// </summary>
/// <remarks>
/// One per event: <see cref="ChannelFullName"/> and <see cref="ReplayId"/> name it, so an event replayed after a
/// restart (delivery is at-least-once) that fails again counts an attempt rather than adding a second failure.
/// Unlike a Writeback Failure it never clears itself: a CDC UPDATE carries only the fields that changed, so a
/// later event for the same record that applies does not fill the gap this one left.
/// </remarks>
public class InboundFailure {
    public int Id { get; set; }
    public int BindingId { get; set; }
    public required string ChannelFullName { get; set; }

    /// <summary>The raw position Salesforce stamped on the event.</summary>
    public required byte[] ReplayId { get; set; }

    public required string EntityName { get; set; }

    /// <summary>CREATE, UPDATE, DELETE or UNDELETE; null when the event's header could not be read.</summary>
    public string? ChangeType { get; set; }

    /// <summary>Every record the event covered; empty when the event could not be decoded far enough to say.</summary>
    public string[] RecordIds { get; set; } = [];

    public required string Message { get; set; }

    /// <summary>The table the refused write was for; null when the event never reached the Target Database.</summary>
    public string? TargetTable { get; set; }

    /// <summary>The SQLSTATE the Target Database refused the write with (22001 value too long, 23502 null, ...).</summary>
    public string? SqlState { get; set; }

    /// <summary>The column the database named, when it named one (a not-null violation does, a value too long does not).</summary>
    public string? TargetColumn { get; set; }

    /// <summary>The constraint the database named, for a unique, foreign key or check violation.</summary>
    public string? ConstraintName { get; set; }

    /// <summary>The column values that were being written, as a JSON object, so the user can see what was refused.</summary>
    public string ValuesSent { get; set; } = "{}";
    public DateTime FirstFailedAt { get; set; }
    public DateTime LastFailedAt { get; set; }
    public int Attempts { get; set; } = 1;
}
