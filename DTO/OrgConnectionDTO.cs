namespace DTO;

/// <summary>
/// The Org Connection as the API reports it.
/// </summary>
/// <remarks>
/// Every field here is safe to show. The Signing Keypair's private key and the Bootstrap Consumer Secret are
/// absent by construction rather than by filtering, so a later edit cannot accidentally reintroduce them —
/// this replaces an endpoint that returned the entire configuration, connection strings included.
/// </remarks>
public record OrgConnectionDTO {
    /// <summary>False on a fresh install. Every other field is then empty.</summary>
    public bool Exists { get; set; }

    /// <summary>"Incomplete", "Connected" or "Failed".</summary>
    public string ConnectionState { get; set; } = "Incomplete";

    public string ConsumerKey { get; set; } = "";
    public string AdministeringUsername { get; set; } = "";
    public string RunAsUsername { get; set; } = "";
    public bool IsSandbox { get; set; }

    /// <summary>Discovered on the first successful token exchange. Null before that.</summary>
    public string? OrgUrl { get; set; }

    /// <summary>Discovered alongside <see cref="OrgUrl"/>; also the Pub/Sub tenantid.</summary>
    public string? OrgId { get; set; }

    public DateTime? LastConnectedAt { get; set; }

    /// <summary>The last failure, raw Salesforce text included. Null when the connection is healthy.</summary>
    public OAuthFailureDTO? LastError { get; set; }

    /// <summary>
    /// SHA-256 of the Signing Certificate. Shown so a user can compare it against what Salesforce holds — a
    /// mismatch here is the usual cause of an assertion Salesforce will not accept.
    /// </summary>
    public string CertificateFingerprint { get; set; } = "";

    public DateTime? CertificateExpiresAt { get; set; }

    /// <summary>
    /// The callback URL to paste into the External Client App, verbatim. It comes from this application's
    /// configuration, never from the incoming request, so what is shown is what Salesforce must be told.
    /// </summary>
    public string CallbackUrl { get; set; } = "";

    /// <summary>
    /// True while the Consumer Secret is stored, so the browser approval can run. Discarded on the first success.
    /// </summary>
    public bool HasConsumerSecret { get; set; }

    /// <summary>
    /// True while the Bootstrap refresh token is held: the Administering User approved, and no JWT token has
    /// succeeded since. Tells "ready to approve" apart from "approved, not working yet".
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>What the last Bootstrap's Self-Configuration did, or null when no Bootstrap has completed.</summary>
    public SelfConfigurationDTO? SelfConfiguration { get; set; }

    /// <summary>Whether stored secrets can currently be read. See <see cref="SecretProtectionDTO"/>.</summary>
    public SecretProtectionDTO SecretProtection { get; set; } = new();
}
