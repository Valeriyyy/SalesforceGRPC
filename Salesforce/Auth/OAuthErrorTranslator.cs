using Newtonsoft.Json.Linq;

namespace Salesforce.Auth;

/// <summary>
/// A rejected OAuth request, as both Salesforce said it and as a user can act on it.
/// </summary>
public sealed record SalesforceOAuthError {
    /// <summary>Salesforce's <c>error</c> field, or "unknown_error" when the body was not parseable.</summary>
    public required string Error { get; init; }

    /// <summary>Salesforce's <c>error_description</c>, verbatim.</summary>
    public required string ErrorDescription { get; init; }

    /// <summary>The complete response body, untouched.</summary>
    public required string RawResponse { get; init; }

    /// <summary>Which setup step this most likely points at, or null when the error is not recognised.</summary>
    public string? Guidance { get; init; }

    /// <summary>A one-line summary safe to store in the connection's LastError.</summary>
    public string Summary => Guidance is null
        ? $"{Error}: {ErrorDescription}"
        : $"{Error}: {ErrorDescription} — {Guidance}";
}

/// <summary>Thrown when Salesforce rejects a token or authorization request.</summary>
public sealed class SalesforceOAuthException : Exception {
    public SalesforceOAuthException(SalesforceOAuthError error) : base(error.Summary) {
        Error = error;
    }

    public SalesforceOAuthError Error { get; }
}

/// <summary>
/// Turns Salesforce's OAuth errors into the setup step that caused them.
/// </summary>
/// <remarks>
/// The application cannot pre-flight any of this. Checking whether the External Client App opt-in is enabled,
/// or whether the permission set reached the Run-as User, needs a session it does not have yet — so every
/// diagnosis is reactive, read back out of the token endpoint's response. This table is what makes guided
/// setup more than a form and a hope.
/// <para>
/// Two rules, both of which exist because the table will always be incomplete. An unrecognised error passes
/// through with no guidance rather than falling back to something generic, and the raw response is carried
/// alongside every translation rather than replaced by it.
/// </para>
/// </remarks>
public static class OAuthErrorTranslator {
    /// <summary>
    /// Parses and translates a token endpoint error body.
    /// </summary>
    /// <param name="rawResponse">The response body exactly as Salesforce sent it.</param>
    /// <param name="certificateFingerprint">
    /// The Signing Certificate's fingerprint, quoted in the guidance where a certificate mismatch is a likely
    /// cause — comparing it against Setup is the fastest way to confirm or rule that out.
    /// </param>
    /// <param name="isSandbox">Whether this connection targets a sandbox, so the audience advice names the right pair.</param>
    public static SalesforceOAuthError Translate(string rawResponse, string? certificateFingerprint = null,
        bool isSandbox = false) {
        var (error, description) = Parse(rawResponse);

        return new SalesforceOAuthError {
            Error = error,
            ErrorDescription = description,
            RawResponse = rawResponse,
            Guidance = Explain(error, description, certificateFingerprint, SalesforceLoginHost.For(isSandbox))
        };
    }

    private static (string Error, string Description) Parse(string rawResponse) {
        if (string.IsNullOrWhiteSpace(rawResponse)) {
            return ("unknown_error", "Salesforce returned an empty response body.");
        }

        try {
            var body = JObject.Parse(rawResponse);
            var error = body.Value<string>("error");
            var description = body.Value<string>("error_description");

            if (!string.IsNullOrWhiteSpace(error)) {
                return (error, description ?? "");
            }
        } catch (Newtonsoft.Json.JsonException) {
            // An HTML error page or a proxy's plain-text response. The body still goes back untouched.
        }

        return ("unknown_error", rawResponse.Length > 500 ? rawResponse[..500] : rawResponse);
    }

    private static string? Explain(string error, string description, string? fingerprint, SalesforceLoginHost host) {
        var text = description.ToLowerInvariant();

        return error.ToLowerInvariant() switch {
            "invalid_grant" when text.Contains("user hasn't approved this consumer")
                                 || text.Contains("user hasn't approved this consumer.") =>
                "The Run-as User is not pre-authorized for the External Client App. Check that the permission " +
                "set is assigned to them and that the app's permitted-users policy is admin-approved. A " +
                "metadata deploy is eventually consistent, so if this was just configured, retry in a minute " +
                "before changing anything.",

            "invalid_grant" when text.Contains("invalid assertion")
                                 || text.Contains("invalid_assertion")
                                 || text.Contains("audience") =>
                BuildAssertionGuidance(fingerprint, host),

            "invalid_grant" when text.Contains("inactive user") || text.Contains("user is inactive") =>
                "The Run-as User is deactivated in Salesforce. Reactivate them, or point the connection at a " +
                "different user.",

            "invalid_grant" when text.Contains("expired") =>
                "The assertion expired before Salesforce processed it. This normally means the clock on this " +
                "host has drifted; check time synchronisation.",

            // Salesforce reports two unrelated causes in one message here, and names the less likely one
            // first. Both are checked because the text cannot tell them apart.
            "invalid_request" when text.Contains("refresh_token scope is required")
                                  || text.Contains("should be installed and preauthorized") =>
                "The External Client App is missing something the JWT Bearer flow requires. Check, in this " +
                "order: the app's OAuth scopes include 'Perform requests at any time' (refresh_token) — this " +
                "flow requires that scope even though it never issues a refresh token; the JWT Bearer flow is " +
                "enabled in the app's flow settings; the permitted-users policy is admin-approved; and a " +
                "permission set granting the app is assigned to the user. Verification signs for the " +
                "Administering User as well as the Run-as User, so this can be the second user failing while " +
                "the first is fine. Policy changes propagate with a delay — retry in a minute before changing " +
                "anything else.",

            "invalid_client_id" or "invalid_client" =>
                "The Consumer Key does not match an External Client App in this org. Copy it again from the " +
                "app in Setup, or confirm the app still exists.",

            "invalid_client_credentials" =>
                "Salesforce rejected the Consumer Key or Secret. If this happened during the initial browser " +
                "approval, the Consumer Secret is wrong; copy both again from the External Client App.",

            "redirect_uri_mismatch" =>
                "The callback URL registered on the External Client App does not exactly match the one this " +
                "application is configured with. Copy the callback URL shown on the setup screen verbatim, " +
                "including scheme, port and trailing path.",

            "inactive_user" =>
                "The Run-as User is deactivated in Salesforce.",

            "inactive_org" =>
                "The Salesforce org is inactive, locked or expired. A sandbox that has not been refreshed in " +
                "a long time can present this way.",

            // Deliberately no default. An unrecognised error travels with its raw text and nothing else,
            // because a confident wrong explanation costs more than an honest absent one.
            _ => null
        };
    }

    private static string BuildAssertionGuidance(string? fingerprint, SalesforceLoginHost host) {
        var guidance =
            "Salesforce would not accept the signed assertion. This connection is marked " +
            $"{host.Description}, so the audience is {host.Url}; if the org is actually " +
            $"{host.Opposite.Description} the audience should be {host.Opposite.Url} and the " +
            "production/sandbox setting is wrong. Otherwise the certificate Salesforce holds does not " +
            "match this application's Signing Keypair.";

        return fingerprint is null
            ? guidance
            : $"{guidance} This application's certificate fingerprint is {fingerprint} — compare it against " +
              "the certificate on the External Client App in Setup.";
    }
}
