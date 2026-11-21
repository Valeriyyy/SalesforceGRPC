namespace DTO;

/// <summary>Sets where the worker begins a channel that has no Checkpoint.</summary>
public record SetStartingPointDTO {
    /// <summary>Latest or Earliest.</summary>
    public string StartingPoint { get; set; } = "";
}
