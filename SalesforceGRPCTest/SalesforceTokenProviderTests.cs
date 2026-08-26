using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Salesforce.Auth;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers the token provider against a fake token endpoint: caching, the two identities, and whether the
/// connection's state is told the truth about what happened.
/// </summary>
/// <remarks>
/// No test here reaches a real Salesforce org. A test only one person can run will rot, and the things worth
/// pinning — which identity got which token, whether a failure was recorded — are all observable locally.
/// </remarks>
public class SalesforceTokenProviderTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RSA Key = RSA.Create(2048);

    private readonly FakeTokenEndpoint _endpoint = new();
    private readonly IOrgConnectionSource _connections = Substitute.For<IOrgConnectionSource>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    public SalesforceTokenProviderTests() {
        _connections.GetConnectionAsync(Arg.Any<CancellationToken>()).Returns(new OrgConnectionDetails {
            ConsumerKey = "3MVG9key",
            AdministeringUsername = "admin@example.com",
            RunAsUsername = "integration@example.com",
            SigningPrivateKeyPem = Key.ExportPkcs8PrivateKeyPem(),
            LoginHost = SalesforceLoginHost.Production,
            CertificateFingerprint = "AA:BB"
        });
    }

    private SalesforceTokenProvider NewProvider() =>
        new(_endpoint.Factory, _connections, NullLogger<SalesforceTokenProvider>.Instance, _time);

    [Fact]
    public async Task ASuccessfulExchange_ReturnsTheTokenAndRecordsTheDiscoveredOrg() {
        _endpoint.RespondWithToken();

        var token = await NewProvider().GetAuthToken(SalesforceIdentity.RunAsUser, Ct);

        Assert.Equal("00Dxx!token", token.AccessToken);
        await _connections.Received(1).RecordTokenSuccessAsync(
            "https://example.my.salesforce.com", "00D000000000001AAA", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARequestUsesTheJwtBearerGrantAndSendsNoPassword() {
        _endpoint.RespondWithToken();

        await NewProvider().GetAuthToken(SalesforceIdentity.RunAsUser, Ct);

        Assert.Equal(JwtAssertionFactory.GrantType, _endpoint.LastForm["grant_type"]);
        Assert.True(_endpoint.LastForm.ContainsKey("assertion"));
        Assert.DoesNotContain("password", _endpoint.LastForm.Keys);
        Assert.DoesNotContain("client_secret", _endpoint.LastForm.Keys);
    }

    [Fact]
    public async Task ASecondRequestForTheSameIdentity_IsServedFromCache() {
        _endpoint.RespondWithToken();
        var provider = NewProvider();

        await provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct);
        await provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct);

        Assert.Equal(1, _endpoint.RequestCount);
    }

    /// <summary>
    /// The <c>sub</c> claim names a user, so sharing one cache entry between the identities would hand the
    /// event stream an administrator's token.
    /// </summary>
    [Fact]
    public async Task TheTwoIdentitiesAreCachedSeparately() {
        _endpoint.RespondWithToken();
        var provider = NewProvider();

        await provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct);
        await provider.GetAuthToken(SalesforceIdentity.AdministeringUser, Ct);

        Assert.Equal(2, _endpoint.RequestCount);
        Assert.Equal(["integration@example.com", "admin@example.com"], _endpoint.SubjectsSeen);
    }

    [Fact]
    public async Task AnExpiredTokenIsReplaced() {
        _endpoint.RespondWithToken(expiresInSeconds: 600);
        var provider = NewProvider();

        await provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct);
        _time.Advance(TimeSpan.FromMinutes(11));
        await provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct);

        Assert.Equal(2, _endpoint.RequestCount);
    }

    [Fact]
    public async Task ARejectedExchange_RecordsTheFailureAndCarriesTheRawResponse() {
        _endpoint.RespondWithError("""{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}""");

        var ex = await Assert.ThrowsAsync<SalesforceOAuthException>(
            () => NewProvider().GetAuthToken(SalesforceIdentity.RunAsUser, Ct));

        Assert.Equal("invalid_grant", ex.Error.Error);
        Assert.Contains("user hasn't approved this consumer", ex.Error.RawResponse);
        Assert.NotNull(ex.Error.Guidance);

        // The raw Salesforce body has to survive the failure path, not just the translation of it.
        await _connections.Received(1).RecordTokenFailureAsync(
            Arg.Is<SalesforceOAuthError>(e =>
                e.Error == "invalid_grant" && e.RawResponse.Contains("user hasn't approved this consumer")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedExchangeIsNotCached() {
        _endpoint.RespondWithError("""{"error":"invalid_grant","error_description":"invalid assertion"}""");
        var provider = NewProvider();

        await Assert.ThrowsAsync<SalesforceOAuthException>(() => provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct));
        await Assert.ThrowsAsync<SalesforceOAuthException>(() => provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct));

        Assert.Equal(2, _endpoint.RequestCount);
    }

    [Fact]
    public async Task WithNoOrgConnection_TheFailureSaysSetupIsMissingRatherThanBlamingSalesforce() {
        _connections.GetConnectionAsync(Arg.Any<CancellationToken>()).Returns((OrgConnectionDetails?)null);

        await Assert.ThrowsAsync<NoOrgConnectionException>(
            () => NewProvider().GetAuthToken(SalesforceIdentity.RunAsUser, Ct));

        Assert.Equal(0, _endpoint.RequestCount);
    }

    /// <summary>
    /// The org-mismatch refusal says "do not connect". A token cached before the refusal would stay live and
    /// get used by the auth handler, so the connection this application refused would work anyway.
    /// </summary>
    [Fact]
    public async Task ATokenRefusedForTheWrongOrg_IsNotLeftInTheCache() {
        _endpoint.RespondWithToken();
        _connections
            .When(c => c.RecordTokenSuccessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("this token is for a different org"));

        var provider = NewProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct));

        // A cached token would have made the second call never reach Salesforce.
        Assert.Equal(2, _endpoint.RequestCount);
    }

    [Fact]
    public async Task ClearCache_ForcesTheNextRequestBackToSalesforce() {
        _endpoint.RespondWithToken();
        var provider = NewProvider();

        await provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct);
        provider.ClearCache();
        await provider.GetAuthToken(SalesforceIdentity.RunAsUser, Ct);

        Assert.Equal(2, _endpoint.RequestCount);
    }

    /// <summary>
    /// A token endpoint that records what it was sent and answers however the test asks it to.
    /// </summary>
    private sealed class FakeTokenEndpoint {
        private readonly Handler _handler = new();

        public IHttpClientFactory Factory { get; }

        public FakeTokenEndpoint() {
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, disposeHandler: false));
            Factory = factory;
        }

        public int RequestCount => _handler.RequestCount;
        public Dictionary<string, string> LastForm => _handler.LastForm;
        public List<string> SubjectsSeen => _handler.SubjectsSeen;

        public void RespondWithToken(int? expiresInSeconds = null) =>
            _handler.Respond(HttpStatusCode.OK, BuildTokenBody(expiresInSeconds));

        public void RespondWithError(string body) => _handler.Respond(HttpStatusCode.BadRequest, body);

        private static string BuildTokenBody(int? expiresInSeconds) {
            var expiresIn = expiresInSeconds is null ? "" : $",\"expires_in\":{expiresInSeconds}";
            return "{\"access_token\":\"00Dxx!token\"," +
                   "\"instance_url\":\"https://example.my.salesforce.com\"," +
                   "\"id\":\"https://login.salesforce.com/id/00D000000000001AAA/005000000000001AAA\"," +
                   "\"token_type\":\"Bearer\"" + expiresIn + "}";
        }

        private sealed class Handler : HttpMessageHandler {
            private HttpStatusCode _status = HttpStatusCode.OK;
            private string _body = "{}";

            public int RequestCount { get; private set; }
            public Dictionary<string, string> LastForm { get; private set; } = [];
            public List<string> SubjectsSeen { get; } = [];

            public void Respond(HttpStatusCode status, string body) {
                _status = status;
                _body = body;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken) {
                RequestCount++;

                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                LastForm = form.Split('&')
                    .Select(pair => pair.Split('=', 2))
                    .ToDictionary(parts => WebUtility.UrlDecode(parts[0]), parts => WebUtility.UrlDecode(parts[1]));

                if (LastForm.TryGetValue("assertion", out var assertion)) {
                    SubjectsSeen.Add(SubjectOf(assertion));
                }

                return new HttpResponseMessage(_status) {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                };
            }

            private static string SubjectOf(string assertion) {
                var payload = assertion.Split('.')[1].Replace('-', '+').Replace('_', '/');
                var json = Encoding.UTF8.GetString(
                    Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')));
                return Newtonsoft.Json.Linq.JObject.Parse(json).Value<string>("sub")!;
            }
        }
    }
}
