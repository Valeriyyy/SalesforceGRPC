using Application.Connections;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace StarPush.Tests;

/// <summary>
/// Covers the keypair the application generates for itself — its identity to Salesforce.
/// </summary>
public class SigningKeypairTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACertificateIsValidForFiveYears() {
        var keypair = SigningKeypairFactory.Create(Now);

        // Five years is the mitigation for having no rotation, so the number is worth pinning.
        Assert.InRange(keypair.ExpiresAt, Now.AddYears(4).UtcDateTime, Now.AddYears(6).UtcDateTime);
    }

    [Fact]
    public void ThePrivateKeyAndCertificateAreExportedAsPem() {
        var keypair = SigningKeypairFactory.Create(Now);

        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", keypair.PrivateKeyPem);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", keypair.CertificatePem);
    }

    /// <summary>
    /// Colon-separated uppercase hex, so a user comparing this against Salesforce's Setup screen is comparing
    /// like with like rather than two renderings of the same bytes.
    /// </summary>
    [Fact]
    public void TheFingerprintIsColonSeparatedUppercaseHex() {
        var keypair = SigningKeypairFactory.Create(Now);

        var octets = keypair.Fingerprint.Split(':');

        Assert.Equal(32, octets.Length);
        Assert.All(octets, octet => Assert.Matches("^[0-9A-F]{2}$", octet));
    }

    [Fact]
    public void ThePrivateKeyCanSignAnAssertionTheCertificateVerifies() {
        var keypair = SigningKeypairFactory.Create(Now);

        using var privateKey = RSA.Create();
        privateKey.ImportFromPem(keypair.PrivateKeyPem);

        using var certificate = X509Certificate2.CreateFromPem(keypair.CertificatePem);
        using var publicKey = certificate.GetRSAPublicKey()!;

        var data = Encoding.UTF8.GetBytes("assertion");
        var signature = privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.True(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void EachCertificateIsDistinct() {
        Assert.NotEqual(
            SigningKeypairFactory.Create(Now).Fingerprint,
            SigningKeypairFactory.Create(Now).Fingerprint);
    }
}
