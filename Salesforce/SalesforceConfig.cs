namespace Salesforce;

/// <summary>
/// Deployment-level Salesforce settings — properties of this installation, not of the user's org.
/// </summary>
/// <remarks>
/// Everything org-specific now lives in the App Database as the Org Connection: credentials, org URL, org id
/// and the channel to subscribe to. Nothing recoverable is left in appsettings.json, which is the point of
/// the feature.
/// <para>
/// There is deliberately no path that seeds an Org Connection from this section when the database has none.
/// It would recreate the plaintext-secrets-in-a-file problem and give two sources of truth that will
/// eventually disagree.
/// </para>
/// </remarks>
public class SalesforceConfig {
    /// <summary>
    /// The Salesforce API version used for REST, Tooling and Metadata calls.
    /// </summary>
    /// <remarks>
    /// Channel management needs 47.0 or later; 56.0 for member filter expressions, 61.0 for channel
    /// EventType, and 60.0 or later for the External Client App certificate field.
    /// </remarks>
    public string ApiVersion { get; set; } = "61.0";

    /// <summary>
    /// The Pub/Sub API endpoint.
    /// </summary>
    /// <remarks>
    /// The default is correct for sandbox and production alike — there is no separate sandbox host. It is
    /// configurable only because orgs under EU data residency must use api.deu.pubsub.salesforce.com, which
    /// nothing in the application could previously express.
    /// </remarks>
    public string PubSubEndpoint { get; set; } = "https://api.pubsub.salesforce.com:7443";

    /// <summary>
    /// The OAuth callback URL registered on the External Client App, e.g.
    /// <c>https://sync.example.com/api/orgconnection/bootstrap/callback</c>.
    /// </summary>
    /// <remarks>
    /// Configured rather than derived from the incoming request's Host header. A reverse proxy, a load
    /// balancer or a spoofed header would otherwise change where Salesforce sends an authorization code, and
    /// this is the one place in the application where an unexpected redirect has real consequences.
    /// <para>
    /// Shown verbatim on the setup screen so the user pastes exactly this into Salesforce; a mismatch here is
    /// the most common setup failure.
    /// </para>
    /// </remarks>
    public string? CallbackUrl { get; set; }

    /// <summary>
    /// Returns why <see cref="CallbackUrl"/> is unusable, or null when it is fine.
    /// </summary>
    /// <remarks>
    /// Checked here rather than left to Salesforce, because Salesforce's rejection arrives only after the user
    /// has created an app, pasted keys and opened a browser — and says nothing more than that the redirect URI
    /// does not match.
    /// </remarks>
    public string? ValidateCallbackUrl() {
        if (string.IsNullOrWhiteSpace(CallbackUrl)) {
            return "SalesforceConfig:CallbackUrl is not configured, so there is nowhere for Salesforce to send " +
                   "the authorization code. Set it to this application's public callback URL and register the " +
                   "same value on the External Client App.";
        }

        if (!Uri.TryCreate(CallbackUrl, UriKind.Absolute, out var callbackUrl)) {
            return $"SalesforceConfig:CallbackUrl is not a valid URL: {CallbackUrl}";
        }

        // Salesforce requires https for callback URLs, with localhost the only exception.
        if (callbackUrl.Scheme != Uri.UriSchemeHttps && !callbackUrl.IsLoopback) {
            return "SalesforceConfig:CallbackUrl must use https — Salesforce accepts http only for localhost. " +
                   $"It is currently {CallbackUrl}.";
        }

        return null;
    }
}
