namespace Salesforce.Auth;

/// <summary>
/// The one-time browser exchange that gives the application its first Salesforce session.
/// </summary>
/// <remarks>
/// An OAuth 2.0 authorization code flow with PKCE against the External Client App the user created by hand. It
/// exists because nothing else can get a session in a customer's org without one: the Metadata API needs a
/// session, a session needs a registration, and a registration needs the Metadata API. See docs/adr/0003 for
/// every route that is closed.
/// <para>
/// The session it returns is borrowed, not kept. It is used to configure the org and then discarded once the
/// application's own JWT credentials work.
/// </para>
/// </remarks>
public interface IBootstrapOAuthClient {
    /// <summary>
    /// The URL to send the Administering User's browser to, carrying the PKCE challenge for
    /// <paramref name="codeVerifier"/>.
    /// </summary>
    string BuildAuthorizeUrl(SalesforceLoginHost host, string consumerKey, string callbackUrl, string state,
        string codeVerifier);

    /// <summary>
    /// Exchanges an authorization code for an access token, proving with <paramref name="codeVerifier"/> that
    /// this is the application that started the authorization.
    /// </summary>
    /// <exception cref="SalesforceOAuthException">Salesforce rejected the exchange.</exception>
    Task<AuthToken> ExchangeCodeAsync(SalesforceLoginHost host, string consumerKey, string consumerSecret,
        string callbackUrl, string code, string codeVerifier, CancellationToken cancellationToken = default);
}
