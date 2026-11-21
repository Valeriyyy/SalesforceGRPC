namespace Salesforce.Auth;

/// <summary>
/// The Org Connection as this project needs it, with its one secret already decrypted.
/// </summary>
/// <remarks>
/// Defined here, rather than the token provider taking the stored row directly, because this project
/// deliberately knows nothing about the App Database — the Application project implements
/// <see cref="IOrgConnectionSource"/> over what is stored.
/// </remarks>
public sealed record OrgConnectionDetails {
    /// <summary>The External Client App's Consumer Key — the assertion's <c>iss</c> claim.</summary>
    public required string ConsumerKey { get; init; }

    public required string AdministeringUsername { get; init; }
    public required string RunAsUsername { get; init; }

    /// <summary>The Signing Keypair's private key, PKCS#8 PEM. Decrypted; do not log it.</summary>
    public required string SigningPrivateKeyPem { get; init; }

    /// <summary>Which Salesforce host this org authenticates against.</summary>
    public required SalesforceLoginHost LoginHost { get; init; }

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
