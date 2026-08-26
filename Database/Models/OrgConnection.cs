namespace Database.Models;

/// <summary>
/// Where an Org Connection stands with Salesforce.
/// </summary>
/// <remarks>
/// Three states rather than a pair of booleans, so "is set up" and "actually works" can never disagree. Incomplete is not an error: a connection holds a real Signing Keypair from the moment the user
/// supplies their details, and the keypair has to exist before Salesforce can be told about it.
/// </remarks>
public enum ConnectionState {
    /// <summary>Details and a Signing Keypair are stored, but no token has ever succeeded.</summary>
    Incomplete,

    /// <summary>A JWT token succeeded, and OrgUrl and OrgId are known.</summary>
    Connected,

    /// <summary>A token attempt failed. LastError says how.</summary>
    Failed
}

/// <summary>
/// The single row in salesforce.org_connection: how this application authenticates to one Salesforce org.
/// </summary>
/// <remarks>
/// <see cref="SigningPrivateKey"/>, <see cref="BootstrapConsumerSecret"/> and
/// <see cref="BootstrapRefreshToken"/> hold Data Protection ciphertext, exactly as they sit in the database.
/// Nothing here decrypts them — that happens a layer up — so an instance of this type is safe to log or to
/// hand around, and a read model built from it leaks nothing as long as those three are left out.
/// </remarks>
public class OrgConnection {
    public int Id { get; set; }

    /// <summary>The External Client App's Consumer Key. Not a secret: it is the JWT <c>iss</c> claim.</summary>
    public required string ConsumerKey { get; set; }

    /// <summary>The user who approves the Bootstrap and under whom setup-level work runs.</summary>
    public required string AdministeringUsername { get; set; }

    /// <summary>The user the event stream runs as. Their permissions bound what the worker can see.</summary>
    public required string RunAsUsername { get; set; }

    /// <summary>Selects test.salesforce.com over login.salesforce.com.</summary>
    public bool IsSandbox { get; set; }

    /// <summary>Data Protection ciphertext of the private key PEM. Never a plaintext PEM.</summary>
    public required string SigningPrivateKey { get; set; }

    /// <summary>The Signing Certificate as PEM. Public by nature — Salesforce holds the same bytes.</summary>
    public required string SigningCertificate { get; set; }

    /// <summary>SHA-256 of the certificate, colon-separated hex, matching what Salesforce shows in Setup.</summary>
    public required string CertificateFingerprint { get; set; }

    public DateTime CertificateExpiresAt { get; set; }

    /// <summary>Discovered from the first successful token response, never entered by the user.</summary>
    public string? OrgUrl { get; set; }

    /// <summary>Discovered alongside <see cref="OrgUrl"/>; feeds the Pub/Sub <c>tenantid</c> header.</summary>
    public string? OrgId { get; set; }

    public ConnectionState ConnectionState { get; set; }

    public DateTime? LastConnectedAt { get; set; }

    /// <summary>The translated one-line summary of the last failure.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Salesforce's response body for the last failure, untouched.
    /// </summary>
    /// <remarks>
    /// Kept alongside the translation rather than instead of it. The translation table is incomplete by
    /// construction, so this is the only text a user can search for or paste into a support conversation —
    /// and a summary cannot be un-summarised later.
    /// </remarks>
    public string? LastErrorRaw { get; set; }

    public DateTime? LastErrorAt { get; set; }

    /// <summary>
    /// Data Protection ciphertext of the Consumer Secret the user pasted for the Bootstrap, or null once the
    /// first JWT token has succeeded.
    /// </summary>
    public string? BootstrapConsumerSecret { get; set; }

    /// <summary>Data Protection ciphertext of the Bootstrap refresh token, purged alongside the secret.</summary>
    public string? BootstrapRefreshToken { get; set; }

    public DateTime DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }

    /// <summary>True when a token has succeeded and the discovered org details are known.</summary>
    public bool IsUsable => ConnectionState == ConnectionState.Connected
                            && !string.IsNullOrWhiteSpace(OrgUrl)
                            && !string.IsNullOrWhiteSpace(OrgId);

    public override string ToString() => $"{ConsumerKey} {RunAsUsername} {ConnectionState}";
}
