using Salesforce.Auth;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers the translation from Salesforce's OAuth errors to the setup step that caused them.
/// </summary>
/// <remarks>
/// Two properties matter more than any individual row of the table: the raw response always survives, and an
/// unrecognised error is never dressed up as a recognised one.
/// </remarks>
public class OAuthErrorTranslationTests {
    private const string Fingerprint = "AA:BB:CC:DD";

    private static string Body(string error, string description) =>
        $$"""{"error":"{{error}}","error_description":"{{description}}"}""";

    [Fact]
    public void UnapprovedConsumer_PointsAtPreAuthorizationAndPropagationDelay() {
        var result = OAuthErrorTranslator.Translate(Body("invalid_grant", "user hasn't approved this consumer"));

        Assert.Contains("permission set", result.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retry", result.Guidance!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidAssertion_NamesTheAudienceItUsedAndTheCertificateFingerprint() {
        var result = OAuthErrorTranslator.Translate(
            Body("invalid_grant", "invalid assertion"), Fingerprint, isSandbox: true);

        Assert.Contains("https://test.salesforce.com", result.Guidance!);
        Assert.Contains(Fingerprint, result.Guidance!);
    }

    [Fact]
    public void InvalidClientId_PointsAtTheConsumerKey() {
        var result = OAuthErrorTranslator.Translate(Body("invalid_client_id", "client identifier invalid"));

        Assert.Contains("Consumer Key", result.Guidance!);
    }

    [Fact]
    public void InactiveUser_PointsAtTheRunAsUser() {
        var result = OAuthErrorTranslator.Translate(Body("invalid_grant", "inactive user"));

        Assert.Contains("deactivated", result.Guidance!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedirectUriMismatch_PointsAtTheCallbackUrl() {
        var result = OAuthErrorTranslator.Translate(Body("redirect_uri_mismatch", "redirect_uri must match configuration"));

        Assert.Contains("callback URL", result.Guidance!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Salesforce packs two unrelated causes into one message for this one, so the guidance has to name both.
    /// </summary>
    [Fact]
    public void MissingScopeOrPreAuthorization_NamesTheScopeAndThePolicy() {
        var result = OAuthErrorTranslator.Translate(Body("invalid_request",
            "refresh_token scope is required and the connected app should be installed and preauthorized"));

        Assert.Contains("refresh_token", result.Guidance!);
        Assert.Contains("admin-approved", result.Guidance!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Administering User", result.Guidance!);
    }

    /// <summary>
    /// The table is incomplete by construction. An unrecognised error must arrive with no explanation rather
    /// than a confident wrong one, because a wrong explanation sends the user to the wrong screen.
    /// </summary>
    [Fact]
    public void AnUnrecognisedError_PassesThroughWithNoGuidance() {
        var result = OAuthErrorTranslator.Translate(Body("some_new_error", "something Salesforce added last release"));

        Assert.Null(result.Guidance);
        Assert.Equal("some_new_error", result.Error);
        Assert.Equal("something Salesforce added last release", result.ErrorDescription);
    }

    [Fact]
    public void TheRawResponseIsAlwaysCarried() {
        var body = Body("invalid_grant", "user hasn't approved this consumer");

        var result = OAuthErrorTranslator.Translate(body);

        Assert.Equal(body, result.RawResponse);
        Assert.Contains(result.ErrorDescription, result.Summary);
    }

    /// <summary>A proxy or an HTML error page is not JSON, and must not become an unhandled exception.</summary>
    [Fact]
    public void ABodyThatIsNotJson_IsReportedRatherThanThrown() {
        var result = OAuthErrorTranslator.Translate("<html><body>502 Bad Gateway</body></html>");

        Assert.Equal("unknown_error", result.Error);
        Assert.Contains("502 Bad Gateway", result.RawResponse);
        Assert.Null(result.Guidance);
    }
}
