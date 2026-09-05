using Newtonsoft.Json.Linq;
using Salesforce.Auth;
using System.Security.Cryptography;
using System.Text;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers the assertion the JWT Bearer flow signs.
/// </summary>
/// <remarks>
/// Every claim here is a documented cause of an opaque <c>invalid_grant</c> — Salesforce does not say which
/// one was wrong. Pinning them costs one test each and removes the guesswork from every setup failure.
/// </remarks>
public class JwtAssertionTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly RSA Key = RSA.Create(2048);

    private static OrgConnectionDetails Connection(bool isSandbox = false) => new() {
        ConsumerKey = "3MVG9consumerkey",
        AdministeringUsername = "admin@example.com",
        RunAsUsername = "integration@example.com",
        SigningPrivateKeyPem = Key.ExportPkcs8PrivateKeyPem(),
        LoginHost = SalesforceLoginHost.For(isSandbox)
    };

    private static JObject PayloadOf(string assertion) {
        var payload = assertion.Split('.')[1];
        return JObject.Parse(Encoding.UTF8.GetString(Base64UrlDecode(payload)));
    }

    private static byte[] Base64UrlDecode(string value) {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    [Fact]
    public void Assertion_CarriesTheConsumerKeyAsIssuer() {
        var payload = PayloadOf(JwtAssertionFactory.Create(Connection(), SalesforceIdentity.RunAsUser, Now));

        Assert.Equal("3MVG9consumerkey", payload.Value<string>("iss"));
    }

    /// <summary>
    /// The <c>sub</c> claim is the only difference between the two identities, and it is what lets one
    /// External Client App and one keypair authenticate both.
    /// </summary>
    [Theory]
    [InlineData(SalesforceIdentity.RunAsUser, "integration@example.com")]
    [InlineData(SalesforceIdentity.AdministeringUser, "admin@example.com")]
    public void Assertion_NamesTheRequestedIdentityAsSubject(SalesforceIdentity identity, string expected) {
        var payload = PayloadOf(JwtAssertionFactory.Create(Connection(), identity, Now));

        Assert.Equal(expected, payload.Value<string>("sub"));
    }

    /// <summary>
    /// A production audience against a sandbox is one of the most common setup failures, and Salesforce
    /// reports it as nothing more specific than "invalid assertion".
    /// </summary>
    [Theory]
    [InlineData(false, "https://login.salesforce.com")]
    [InlineData(true, "https://test.salesforce.com")]
    public void Assertion_AudienceFollowsTheSandboxFlag(bool isSandbox, string expected) {
        var payload = PayloadOf(JwtAssertionFactory.Create(Connection(isSandbox), SalesforceIdentity.RunAsUser, Now));

        Assert.Equal(expected, payload.Value<string>("aud"));
    }

    [Fact]
    public void Assertion_ExpiresWithinSalesforcesThreeMinuteLimit() {
        var payload = PayloadOf(JwtAssertionFactory.Create(Connection(), SalesforceIdentity.RunAsUser, Now));

        var expiry = DateTimeOffset.FromUnixTimeSeconds(payload.Value<long>("exp"));

        Assert.True(expiry > Now, "The assertion must not already be expired when it is signed.");
        Assert.True(expiry - Now <= TimeSpan.FromMinutes(3),
            "Salesforce rejects an exp more than three minutes ahead.");
    }

    [Fact]
    public void Assertion_IsSignedRS256AndVerifiesAgainstThePublicKey() {
        var assertion = JwtAssertionFactory.Create(Connection(), SalesforceIdentity.RunAsUser, Now);

        var parts = assertion.Split('.');
        Assert.Equal(3, parts.Length);

        var header = JObject.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
        Assert.Equal("RS256", header.Value<string>("alg"));
        Assert.Equal("JWT", header.Value<string>("typ"));

        using var publicKey = RSA.Create();
        publicKey.ImportSubjectPublicKeyInfo(Key.ExportSubjectPublicKeyInfo(), out _);

        Assert.True(publicKey.VerifyData(
            Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Assertion_ForAnIdentityWithNoUsername_SaysSoRatherThanSigningAnEmptySubject() {
        var connection = Connection() with { AdministeringUsername = "" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtAssertionFactory.Create(connection, SalesforceIdentity.AdministeringUser, Now));

        Assert.Contains("AdministeringUser", ex.Message);
    }
}
