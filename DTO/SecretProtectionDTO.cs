namespace DTO;

/// <summary>
/// Whether the application can read its own stored secrets, and if not, why.
/// </summary>
/// <remarks>
/// The two failure cases must stay distinguishable. "No protecting key and nothing encrypted" is a fresh
/// install and the remedy is to configure one; "ciphertext that will not decrypt" means a key ring went
/// missing, and re-running setup would generate a new Signing Keypair, orphan the External Client App in
/// Salesforce, and hide the real cause.
/// </remarks>
public record SecretProtectionDTO {
    /// <summary>"Ready", "NotConfigured" or "Unreadable".</summary>
    public string Status { get; set; } = "NotConfigured";

    /// <summary>Where the protecting key came from, or where it was looked for. Never key material.</summary>
    public string ProtectingKey { get; set; } = "";

    /// <summary>What to do about it, when there is something to do.</summary>
    public string? Guidance { get; set; }
}
