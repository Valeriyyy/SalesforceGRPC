using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;

namespace Salesforce.Auth;

/// <summary>
/// The one-time browser exchange that gives the application its first Salesforce session.
/// </summary>
/// <remarks>
/// A plain OAuth 2.0 authorization code flow against the External Client App the user created by hand. It
/// exists because nothing else can get a session in a customer's org without one: the Metadata API needs a
/// session, a session needs a registration, and a registration needs the Metadata API. See
/// docs/adr/0003 for every route that is closed.
/// <para>
/// The session it returns is borrowed, not kept. It is used to configure the org and then discarded once the
/// application's own JWT credentials work.
/// </para>
/// </remarks>
public interface IBootstrapOAuthClient {
    /// <summary>The URL to send the Administering User's browser to.</summary>
    string BuildAuthorizeUrl(string loginUrl, string consumerKey, string callbackUrl, string state);

    /// <summary>Exchanges an authorization code for an access token.</summary>
    /// <exception cref="SalesforceOAuthException">Salesforce rejected the exchange.</exception>
    Task<AuthToken> ExchangeCodeAsync(string loginUrl, string consumerKey, string consumerSecret,
        string callbackUrl, string code, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BootstrapOAuthClient : IBootstrapOAuthClient {
    /// <summary>The named HttpClient used for Bootstrap requests.</summary>
    public const string HttpClientName = "SalesforceBootstrap";

    /// <summary>
    /// <c>api</c> to call the Metadata and REST APIs, <c>refresh_token</c> so the borrowed session survives
    /// the propagation delay between a metadata deploy and the first working JWT.
    /// </summary>
    public const string Scopes = "api refresh_token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BootstrapOAuthClient> _logger;

    public BootstrapOAuthClient(IHttpClientFactory httpClientFactory, ILogger<BootstrapOAuthClient> logger) {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string BuildAuthorizeUrl(string loginUrl, string consumerKey, string callbackUrl, string state) {
        var query = string.Join('&', [
            "response_type=code",
            $"client_id={WebUtility.UrlEncode(consumerKey)}",
            $"redirect_uri={WebUtility.UrlEncode(callbackUrl)}",
            $"scope={WebUtility.UrlEncode(Scopes)}",
            $"state={WebUtility.UrlEncode(state)}"
        ]);

        return $"{loginUrl.TrimEnd('/')}/services/oauth2/authorize?{query}";
    }

    public async Task<AuthToken> ExchangeCodeAsync(string loginUrl, string consumerKey, string consumerSecret,
        string callbackUrl, string code, CancellationToken cancellationToken = default) {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"{loginUrl.TrimEnd('/')}/services/oauth2/token") {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("client_id", consumerKey),
                new KeyValuePair<string, string>("client_secret", consumerSecret),
                // Sent again on the exchange, and it must match the authorize request exactly. This is the
                // usual source of redirect_uri_mismatch, which is why both come from one configured value.
                new KeyValuePair<string, string>("redirect_uri", callbackUrl),
                new KeyValuePair<string, string>("code", code)
            ])
        };

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            var error = OAuthErrorTranslator.Translate(
                body, isSandbox: loginUrl.Contains("test.salesforce.com", StringComparison.OrdinalIgnoreCase));
            _logger.LogError("The Bootstrap code exchange failed: {Error}", error.Summary);
            throw new SalesforceOAuthException(error);
        }

        var token = JsonConvert.DeserializeObject<AuthToken>(body);
        if (token?.AccessToken is null || token.InstanceUrl is null) {
            throw new SalesforceOAuthException(OAuthErrorTranslator.Translate(body));
        }

        _logger.LogInformation("Bootstrap session obtained for org {OrgId} at {InstanceUrl}",
            token.OrgId ?? "(unknown)", token.InstanceUrl);

        return token;
    }
}
