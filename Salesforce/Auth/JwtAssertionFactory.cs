using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Salesforce.Auth;

/// <summary>
/// Builds the signed assertion for Salesforce's OAuth 2.0 JWT Bearer flow.
/// </summary>
/// <remarks>
/// Written by hand rather than through a JWT library because the whole thing is four claims and one
/// signature, and every one of those claims is a documented cause of an opaque <c>invalid_grant</c>. Keeping
/// it here keeps it unit testable against a known key.
/// <para>
/// This flow issues no refresh token. Expiry means signing a fresh assertion, which is exactly why the
/// service can reconnect unattended after a restart.
/// </para>
/// </remarks>
public static class JwtAssertionFactory {
    /// <summary>The grant type Salesforce's token endpoint expects for this flow.</summary>
    public const string GrantType = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    /// <summary>
    /// How far ahead <c>exp</c> is set. Salesforce rejects anything more than three minutes out, so this
    /// sits below the limit rather than on it — the assertion still has to travel and be processed, and a
    /// second of clock skew at the boundary reads as a plain "invalid assertion".
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Signs an assertion for one identity.
    /// </summary>
    /// <param name="connection">The Consumer Key, usernames, login host and private key.</param>
    /// <param name="identity">Which user the assertion is for — this is the only thing that differs between the two.</param>
    /// <param name="now">The current time, injected so the <c>exp</c> window is testable.</param>
    public static string Create(OrgConnectionDetails connection, SalesforceIdentity identity, DateTimeOffset now) {
        ArgumentNullException.ThrowIfNull(connection);

        var subject = connection.UsernameFor(identity);
        if (string.IsNullOrWhiteSpace(subject)) {
            throw new InvalidOperationException(
                $"The Org Connection has no username for the {identity}, so no assertion can be signed for it.");
        }

        var header = new { alg = "RS256", typ = "JWT" };

        var claims = new Dictionary<string, object> {
            ["iss"] = connection.ConsumerKey,
            ["sub"] = subject,
            // The audience is login.salesforce.com or test.salesforce.com and nothing else — notably not the
            // org's own instance URL, which is the mistake this claim usually carries.
            ["aud"] = connection.LoginHost.Url,
            ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds()
        };

        var signingInput = $"{Encode(header)}.{Encode(claims)}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(connection.SigningPrivateKeyPem);

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Encode(object value) =>
        Base64UrlEncode(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
