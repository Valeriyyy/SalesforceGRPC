using Microsoft.Extensions.Logging;
using System.Net;

namespace Salesforce.Auth;

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

    public string BuildAuthorizeUrl(SalesforceLoginHost host, string consumerKey, string callbackUrl, string state,
        string codeVerifier) {
        var query = string.Join('&', [
            "response_type=code",
            $"client_id={WebUtility.UrlEncode(consumerKey)}",
            $"redirect_uri={WebUtility.UrlEncode(callbackUrl)}",
            $"scope={WebUtility.UrlEncode(Scopes)}",
            $"state={WebUtility.UrlEncode(state)}",
            $"code_challenge={WebUtility.UrlEncode(Pkce.ChallengeFor(codeVerifier))}",
            $"code_challenge_method={Pkce.ChallengeMethod}"
        ]);

        return $"{host.AuthorizeEndpoint}?{query}";
    }

    public async Task<AuthToken> ExchangeCodeAsync(SalesforceLoginHost host, string consumerKey, string consumerSecret,
        string callbackUrl, string code, string codeVerifier, CancellationToken cancellationToken = default) {
        using var client = _httpClientFactory.CreateClient(HttpClientName);

        AuthToken token;
        try {
            token = await SalesforceTokenEndpoint.PostAsync(client, host, [
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("client_id", consumerKey),
                new KeyValuePair<string, string>("client_secret", consumerSecret),
                // Sent again on the exchange, and it must match the authorize request exactly. This is the
                // usual source of redirect_uri_mismatch, which is why both come from one configured value.
                new KeyValuePair<string, string>("redirect_uri", callbackUrl),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("code_verifier", codeVerifier)
            ], certificateFingerprint: null, cancellationToken).ConfigureAwait(false);
        } catch (SalesforceOAuthException exc) {
            _logger.LogError("The Bootstrap code exchange failed: {Error}", exc.Error.Summary);
            throw;
        }

        _logger.LogInformation("Bootstrap session obtained for org {OrgId} at {InstanceUrl}",
            token.OrgId ?? "(unknown)", token.InstanceUrl);

        return token;
    }
}
