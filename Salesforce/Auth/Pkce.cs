using System.Security.Cryptography;
using System.Text;

namespace Salesforce.Auth;

/// <summary>
/// Proof Key for Code Exchange (RFC 7636): a secret the Bootstrap keeps, and a hash of it sent up front.
/// </summary>
/// <remarks>
/// External Client Apps require it by default, and reject an authorize request without a challenge before the
/// user sees the approval screen. It proves the code exchange comes from whoever started the authorization,
/// so an intercepted authorization code is useless on its own.
/// </remarks>
public static class Pkce {
    /// <summary>The only challenge method used: SHA-256 of the verifier. "plain" would defeat the point.</summary>
    public const string ChallengeMethod = "S256";

    /// <summary>A fresh verifier: 32 random bytes, base64url-encoded to 43 characters.</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The challenge sent on the authorize request for a verifier.</summary>
    public static string ChallengeFor(string codeVerifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
