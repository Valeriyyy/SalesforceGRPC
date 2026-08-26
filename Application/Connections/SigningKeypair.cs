using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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

/// <summary>
/// Generates the Signing Keypair.
/// </summary>
/// <remarks>
/// Generation happens when the user first supplies their connection details, before any Salesforce
/// round-trip. That is unavoidable — the certificate has to exist before Salesforce can be told about it —
/// and it is why an Org Connection can hold a real secret while still being Incomplete.
/// </remarks>
public static class SigningKeypairFactory {
    public const int KeySizeBits = 2048;

    /// <summary>
    /// Five years. There is no keypair rotation in this feature, and the long life is the mitigation rather
    /// than an oversight: rotation is coupled to Disconnect today, which destroys every Binding, so the
    /// coupling has to be broken before rotation is worth building.
    /// </summary>
    public static readonly TimeSpan Validity = TimeSpan.FromDays(365 * 5);

    /// <summary>The certificate subject. Fixed so an administrator can recognise it in Setup.</summary>
    public const string SubjectName = "CN=SalesforceGrpc Org Connection";

    public static SigningKeypair Create(DateTimeOffset now) {
        using var rsa = RSA.Create(KeySizeBits);

        var request = new CertificateRequest(SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

        // Backdated a little so a modest clock difference between this host and Salesforce cannot make a
        // certificate that was just generated look not-yet-valid.
        var notBefore = now.AddMinutes(-5);
        var notAfter = now.Add(Validity);

        using var certificate = request.CreateSelfSigned(notBefore, notAfter);

        return new SigningKeypair {
            PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
            CertificatePem = certificate.ExportCertificatePem(),
            Fingerprint = FormatFingerprint(certificate),
            ExpiresAt = notAfter.UtcDateTime
        };
    }

    /// <summary>
    /// Colon-separated uppercase hex, the form Salesforce shows, so a user comparing the two is comparing
    /// like with like rather than squinting at different renderings of the same bytes.
    /// </summary>
    private static string FormatFingerprint(X509Certificate2 certificate) {
        var hash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        return Convert.ToHexString(hash).Chunk(2).Select(pair => new string(pair)).Aggregate((a, b) => $"{a}:{b}");
    }
}
