using Application.Connections;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Salesforce.Auth;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SalesforceGRPCTest;

/// <summary>
/// The Bootstrap's proof key (PKCE), seen the way Salesforce sees it: the challenge on the authorize request,
/// then the verifier on the code exchange.
/// </summary>
/// <remarks>
/// External Client Apps require PKCE by default, and reject an authorize request without a
/// <c>code_challenge</c> before the user ever sees the approval screen.
/// </remarks>
public class BootstrapPkceTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string CallbackUrl = "https://localhost:5001/api/orgconnection/bootstrap/callback";

    private readonly TokenEndpoint _tokenEndpoint = new();
    private readonly BootstrapStateStore _states = new(TimeProvider.System);
    private readonly BootstrapOAuthClient _client;

    public BootstrapPkceTests() {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(BootstrapOAuthClient.HttpClientName).Returns(_ => new HttpClient(_tokenEndpoint));
        _client = new BootstrapOAuthClient(factory, NullLogger<BootstrapOAuthClient>.Instance);
    }

    private static Dictionary<string, string> QueryOf(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => WebUtility.UrlDecode(parts[1]));

    private static string S256(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void TheAuthorizeRequest_CarriesAnS256CodeChallenge() {
        var request = _states.Issue();

        var query = QueryOf(_client.BuildAuthorizeUrl(SalesforceLoginHost.For(false), "key", CallbackUrl,
            request.State, request.CodeVerifier));

        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(S256(request.CodeVerifier), query["code_challenge"]);
        Assert.Equal(request.State, query["state"]);
    }

    [Fact]
    public async Task TheCodeExchange_SendsTheVerifierTheChallengeWasMadeFrom() {
        var request = _states.Issue();
        var challenge = QueryOf(_client.BuildAuthorizeUrl(SalesforceLoginHost.For(false), "key", CallbackUrl,
            request.State, request.CodeVerifier))["code_challenge"];

        Assert.True(_states.TryConsume(request.State, out var verifier));
        await _client.ExchangeCodeAsync(SalesforceLoginHost.For(false), "key", "secret", CallbackUrl, "code",
            verifier, Ct);

        Assert.Equal(challenge, S256(_tokenEndpoint.LastForm["code_verifier"]));
    }

    [Fact]
    public void EachBootstrap_GetsItsOwnVerifier_OfALengthSalesforceAccepts() {
        var first = _states.Issue();
        var second = _states.Issue();

        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
        // RFC 7636: 43 to 128 characters from the unreserved set.
        Assert.InRange(first.CodeVerifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9._~-]+$", first.CodeVerifier);
    }

    [Fact]
    public void AStateThatWasNeverIssued_YieldsNoVerifier() {
        Assert.False(_states.TryConsume("forged", out var verifier));
        Assert.Null(verifier);
    }

    /// <summary>Stands in for Salesforce's token endpoint and keeps the last form it was posted.</summary>
    private sealed class TokenEndpoint : HttpMessageHandler {
        public Dictionary<string, string> LastForm { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastForm = form.Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(parts => WebUtility.UrlDecode(parts[0]), parts => WebUtility.UrlDecode(parts[1]));

            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(
                    """{"access_token":"00Dxx!token","instance_url":"https://example.my.salesforce.com","id":"https://login.salesforce.com/id/00D000000000001AAA/005000000000001AAA","refresh_token":"r","token_type":"Bearer"}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
