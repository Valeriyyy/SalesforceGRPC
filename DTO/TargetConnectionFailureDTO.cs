namespace DTO;

/// <summary>
/// A Target Database failure, summarised and raw.
/// </summary>
public record TargetConnectionFailureDTO {
    /// <summary>The one-line summary.</summary>
    public string Message { get; set; } = "";

    /// <summary>The driver's message, untouched. The only text a user can search for.</summary>
    public string RawResponse { get; set; } = "";

    public DateTime? OccurredAt { get; set; }
}
