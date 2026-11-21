namespace DTO;

/// <summary>
/// The Target Connection as the API reports it.
/// </summary>
/// <remarks>
/// Every field here is safe to show. The password is absent by construction rather than by filtering, so a
/// later edit cannot accidentally reintroduce it; <see cref="HasPassword"/> says whether one is stored.
/// Everything else is returned in full because the user must be able to review and edit it.
/// </remarks>
public record TargetConnectionDTO {
    /// <summary>False on a fresh install. Every other field is then empty.</summary>
    public bool Exists { get; set; }

    /// <summary>"Incomplete", "Connected" or "Failed".</summary>
    public string ConnectionState { get; set; } = "Incomplete";

    public string Engine { get; set; } = "";
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? Username { get; set; }
    public string? FilePath { get; set; }
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.Ordinal);

    public bool HasPassword { get; set; }

    public DateTime? LastConnectedAt { get; set; }

    /// <summary>The last failure, driver text included. Null when the connection is healthy.</summary>
    public TargetConnectionFailureDTO? LastError { get; set; }

    /// <summary>Whether stored secrets can currently be read.</summary>
    public SecretProtectionDTO SecretProtection { get; set; } = new();

    /// <summary>The Replication Credential, or null when none has been entered. Its password is never returned.</summary>
    public ReplicationCredentialDTO? Replication { get; set; }

    /// <summary>True while this application has the database set up for Writeback.</summary>
    public bool WritebackArmed { get; set; }
}
