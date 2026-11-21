namespace DTO;

/// <summary>What became of a Retry.</summary>
public record WritebackRetryDTO {
    /// <summary>Succeeded, Failed, or Discarded (the row no longer exists).</summary>
    public string Outcome { get; set; } = "";
    public string? Message { get; set; }
}
