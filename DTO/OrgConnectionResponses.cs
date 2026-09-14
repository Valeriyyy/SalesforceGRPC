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

    /// <summary>True while the Bootstrap session is still held, meaning Self-Configuration can be retried.</summary>
    public bool HasBootstrapSession { get; set; }

    /// <summary>Whether stored secrets can currently be read. See <see cref="SecretProtectionDTO"/>.</summary>
    public SecretProtectionDTO SecretProtection { get; set; } = new();
}

/// <summary>
/// A Salesforce OAuth failure, translated and raw.
/// </summary>
/// <remarks>
/// Both halves, always. The translation table will be incomplete, and an unrecognised error that gets
/// replaced by a friendly guess is worse than no translation at all — the raw text is the only thing that can
/// be searched for or pasted into a support conversation.
/// </remarks>
public record OAuthFailureDTO {
    /// <summary>Salesforce's <c>error</c> field, e.g. "invalid_grant".</summary>
    public string Error { get; set; } = "";

    /// <summary>Salesforce's <c>error_description</c>, verbatim.</summary>
    public string ErrorDescription { get; set; } = "";

    /// <summary>What this most likely means about the setup, or null when the error is not recognised.</summary>
    public string? Guidance { get; set; }

    /// <summary>The complete response body, untouched.</summary>
    public string RawResponse { get; set; } = "";

    public DateTime? OccurredAt { get; set; }
}

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

/// <summary>What a Disconnect would destroy, so the user can be asked before it happens.</summary>
public record DisconnectPreviewDTO {
    public int Bindings { get; set; }
    public int FieldMappings { get; set; }
    public int AvroSchemas { get; set; }
    public int Channels { get; set; }
    public int ChannelMembers { get; set; }

    /// <summary>
    /// The Target Connection that will be destroyed too, as engine and address, or null when none is set up.
    /// </summary>
    /// <remarks>
    /// Where data lands is orthogonal to which org it comes from, so this is the one piece of state a user
    /// might not expect a Salesforce Disconnect to take. It is named here so it is disclosed, not discovered.
    /// </remarks>
    public string? TargetConnection { get; set; }

    /// <summary>
    /// What is left standing inside Salesforce, for the user to remove themselves if they want to.
    /// </summary>
    /// <remarks>
    /// Deleting objects in a customer's production org is an outward, hard-to-reverse action, and a partial
    /// remote cleanup during a local wipe leaves a state neither side can describe. An orphaned External
    /// Client App is harmless and auditable; a half-deleted one is not.
    /// </remarks>
    public List<string> LeftInSalesforce { get; set; } = [];
}

/// <summary>The Bootstrap authorize URL for the caller to send the user's browser to.</summary>
public record BootstrapStartDTO {
    public string AuthorizeUrl { get; set; } = "";
}
