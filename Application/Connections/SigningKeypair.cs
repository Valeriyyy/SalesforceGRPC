namespace Application.Connections;

/// <summary>
/// An RSA keypair and its self-signed certificate: the application's own identity to Salesforce.
/// </summary>
/// <remarks>
/// Salesforce holds the certificate and this application holds the private key, which is the whole reason the
/// JWT Bearer flow can reconnect after a restart with nobody present — there is no password to re-enter and
/// no refresh token to have expired.
/// </remarks>
public sealed record SigningKeypair {
    /// <summary>PKCS#8 PEM. The one secret this application stores.</summary>
    public required string PrivateKeyPem { get; init; }

    /// <summary>X.509 PEM, which is what <c>ExtlClntAppGlobalOauthSettings.certificate</c> takes.</summary>
    public required string CertificatePem { get; init; }

    /// <summary>SHA-256, colon-separated uppercase hex, matching how Salesforce shows it in Setup.</summary>
    public required string Fingerprint { get; init; }

    public required DateTime ExpiresAt { get; init; }
}
