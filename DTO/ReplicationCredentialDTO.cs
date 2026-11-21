namespace DTO;

public record ReplicationCredentialDTO {
    public string Username { get; set; } = "";

    /// <summary>Incomplete (never proved), Connected or Failed.</summary>
    public string State { get; set; } = "Incomplete";

    public DateTime? LastProvedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
}
