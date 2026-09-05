using Application.Connections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers encrypting the one secret this application stores, and the two startup cases that must never be
/// conflated.
/// </summary>
public class SecretProtectionTests {
    private static IDataProtectionProvider NewProvider(string applicationName) =>
        new ServiceCollection()
            .AddLogging()
            .AddDataProtection()
            .SetApplicationName(applicationName)
            .UseEphemeralDataProtectionProvider()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

    private static SecretProtector Protector(IDataProtectionProvider? provider, string description = "a test key") =>
        new(provider, description, NullLogger<SecretProtector>.Instance);

    [Fact]
    public void ASecretSurvivesAProtectUnprotectRoundTrip() {
        var protector = Protector(NewProvider("SalesforceGrpc"));
        const string privateKey = "-----BEGIN PRIVATE KEY-----\nnot really a key\n-----END PRIVATE KEY-----";

        var ciphertext = protector.Protect(privateKey);

        Assert.NotEqual(privateKey, ciphertext);
        Assert.Equal(privateKey, protector.Unprotect(ciphertext));
    }

    /// <summary>
    /// A fresh install: nothing is stored and nothing can be. The application must refuse to store rather
    /// than store in the clear.
    /// </summary>
    [Fact]
    public void WithNoProtectingKey_StoringASecretIsRefusedAndSaysWhatToSupply() {
        var protector = Protector(null, "$SALESFORCEGRPC_PROTECTING_CERT: unset");

        Assert.False(protector.IsAvailable);

        var ex = Assert.Throws<SecretProtectionUnavailableException>(() => protector.Protect("anything"));
        Assert.Contains("protecting certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$SALESFORCEGRPC_PROTECTING_CERT: unset", ex.Message);
    }

    /// <summary>
    /// The case that must never read as "not configured yet". Told that, a user re-runs setup, generates a
    /// new Signing Keypair, orphans the External Client App in Salesforce, and buries the real cause.
    /// </summary>
    [Fact]
    public void CiphertextThatWillNotDecrypt_IsADistinctFailureThatWarnsAgainstReRunningSetup() {
        var stored = Protector(NewProvider("SalesforceGrpc")).Protect("the original private key");

        // A different key ring — what an operator sees after losing the protecting certificate.
        var afterKeyLoss = Protector(NewProvider("SalesforceGrpc"));

        var ex = Assert.Throws<SecretsUnreadableException>(() => afterKeyLoss.Unprotect(stored));

        Assert.Contains("could not be decrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orphan", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithNoProtectingKeyButStoredCiphertext_ReadingSaysItIsNotAFreshInstall() {
        var ex = Assert.Throws<SecretsUnreadableException>(
            () => Protector(null).Unprotect("some stored ciphertext"));

        Assert.Contains("not a fresh install", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryUnprotect_ReportsFailureRatherThanThrowing() {
        var stored = Protector(NewProvider("SalesforceGrpc")).Protect("the original private key");
        var afterKeyLoss = Protector(NewProvider("SalesforceGrpc"));

        Assert.False(afterKeyLoss.TryUnprotect(stored, out _));
        Assert.False(Protector(null).TryUnprotect(stored, out _));
    }
}

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
