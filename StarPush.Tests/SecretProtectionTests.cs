using Application.Connections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace StarPush.Tests;

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
        var protector = Protector(null, "$STARPUSH_PROTECTING_CERT: unset");

        Assert.False(protector.IsAvailable);

        var ex = Assert.Throws<SecretProtectionUnavailableException>(() => protector.Protect("anything"));
        Assert.Contains("protecting certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$STARPUSH_PROTECTING_CERT: unset", ex.Message);
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
