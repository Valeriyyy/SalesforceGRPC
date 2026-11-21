namespace Application.Writeback;

/// <summary>What became of one transaction's changes.</summary>
public sealed class WritebackSummary {
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public string? LastFailure { get; set; }
}
