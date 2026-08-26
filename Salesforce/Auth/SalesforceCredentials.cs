namespace Salesforce.Auth;

/// <summary>
/// Which Salesforce user an access token is being requested for.
/// </summary>
/// <remarks>
/// The JWT Bearer flow names a user in the <c>sub</c> claim, so one External Client App and one Signing
/// Keypair authenticate both identities — they differ only in that claim. Two of them exist because the
/// event stream should run under permissions the customer chose, while setup-level work needs an
/// administrator, and conflating them would mean granting the stream more than it needs.
/// </remarks>
public enum SalesforceIdentity {
    /// <summary>The user the event stream runs as. Their permissions bound what the worker can see.</summary>
    RunAsUser,

    /// <summary>The administrator who approved setup, under whom Tooling and Metadata work runs.</summary>
    AdministeringUser
}

/// <summary>
/// Everything the token provider needs to sign an assertion, with the one secret already decrypted.
/// </summary>
/// <remarks>
/// This is the shape of the Org Connection as the Salesforce project sees it. It is defined here, rather than
/// the token provider taking a database type, because the Salesforce project deliberately knows nothing about
/// the App Database — the Application project implements
/// <see cref="ISalesforceCredentialSource"/> over the stored connection.
/// </remarks>
public sealed record SalesforceCredentials {
    /// <summary>The External Client App's Consumer Key — the assertion's <c>iss</c> claim.</summary>
    public required string ConsumerKey { get; init; }

    public required string AdministeringUsername { get; init; }
    public required string RunAsUsername { get; init; }

    /// <summary>The Signing Keypair's private key, PKCS#8 PEM. Decrypted; do not log it.</summary>
    public required string SigningPrivateKeyPem { get; init; }

    /// <summary>
    /// The assertion's <c>aud</c> claim and the token endpoint's authority: login.salesforce.com for
    /// production, test.salesforce.com for a sandbox.
    /// </summary>
    public required string LoginUrl { get; init; }

    /// <summary>The discovered instance URL, or null before the first successful token exchange.</summary>
    public string? OrgUrl { get; init; }

    /// <summary>The discovered org id, or null before the first successful token exchange.</summary>
    public string? OrgId { get; init; }

    /// <summary>The Signing Certificate's SHA-256 fingerprint, for comparing against Salesforce on failure.</summary>
    public string? CertificateFingerprint { get; init; }

    /// <summary>The username for an identity — the assertion's <c>sub</c> claim.</summary>
    public string UsernameFor(SalesforceIdentity identity) => identity switch {
        SalesforceIdentity.RunAsUser => RunAsUsername,
        SalesforceIdentity.AdministeringUser => AdministeringUsername,
        _ => throw new ArgumentOutOfRangeException(nameof(identity), identity, "Unknown Salesforce identity")
    };
}
