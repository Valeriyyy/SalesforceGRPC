using Application.Bindings;
using Application.Connections;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Salesforce.Auth;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers the Org Connection's state machine: what a successful or rejected token does to the stored
/// connection, and the refusal that makes Disconnect the only route between orgs.
/// </summary>
public class OrgConnectionStateTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string OrgId = "00D000000000001AAA";
    private const string OrgUrl = "https://example.my.salesforce.com";

    private readonly IOrgConnectionProvider _provider = Substitute.For<IOrgConnectionProvider>();
    private readonly IOrgConnectionRepository _repository = Substitute.For<IOrgConnectionRepository>();
    private readonly IConfigurationChangeSignal _signal = Substitute.For<IConfigurationChangeSignal>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    private StoredOrgConnectionSource NewSource() =>
        new(_provider, _repository, _signal, NullLogger<StoredOrgConnectionSource>.Instance, _time);

    private static OrgConnection Connection(ConnectionState state = ConnectionState.Incomplete, string? orgId = null) => new() {
        Id = 1,
        ConsumerKey = "3MVG9key",
        AdministeringUsername = "admin@example.com",
        RunAsUsername = "integration@example.com",
        SigningPrivateKey = "ciphertext",
        SigningCertificate = "-----BEGIN CERTIFICATE-----",
        CertificateFingerprint = "AA:BB",
        CertificateExpiresAt = new DateTime(2031, 1, 1),
        ConnectionState = state,
        OrgId = orgId
    };

    private void WithConnection(OrgConnection connection) =>
        _provider.GetAsync(Arg.Any<CancellationToken>()).Returns(connection);

    [Fact]
    public async Task ASuccessfulToken_MovesAnIncompleteConnectionToConnected() {
        WithConnection(Connection());

        await NewSource().RecordTokenSuccessAsync(OrgUrl, OrgId, Ct);

        await _repository.Received(1).RecordSuccessAsync(OrgUrl, OrgId, _time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        _provider.Received().Invalidate();
    }

    /// <summary>
    /// The worker idles while there is no usable connection, so it has to be told rather than discovering
    /// this whenever it next happens to re-plan.
    /// </summary>
    [Fact]
    public async Task TheFirstSuccessfulToken_WakesTheWorker() {
        WithConnection(Connection());

        await NewSource().RecordTokenSuccessAsync(OrgUrl, OrgId, Ct);

        _signal.Received(1).Signal();
    }

    [Fact]
    public async Task ARoutineRefreshOnAConnectedConnection_DoesNotWakeTheWorker() {
        WithConnection(Connection(ConnectionState.Connected, OrgId));

        await NewSource().RecordTokenSuccessAsync(OrgUrl, OrgId, Ct);

        _signal.DidNotReceive().Signal();
    }

    [Fact]
    public async Task ARejectedToken_MovesTheConnectionToFailedAndKeepsTheReason() {
        WithConnection(Connection(ConnectionState.Connected, OrgId));

        var error = OAuthErrorTranslator.Translate(
            """{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}""");

        await NewSource().RecordTokenFailureAsync(error, Ct);

        // Both halves: the translated summary a status line shows, and Salesforce's untouched words, which
        // are the only thing a user can search for when the translation table has nothing to say.
        await _repository.Received(1).RecordFailureAsync(
            Arg.Is<string>(summary => summary.Contains("invalid_grant")),
            Arg.Is<string>(raw => raw.Contains("user hasn't approved this consumer")),
            _time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The guard that makes Disconnect the only route between orgs rather than merely the intended one.
    /// Without it, pointing the Run-as User at another org's user connects cleanly and every Binding starts
    /// writing that org's records into tables mapped for the old one — corruption with no error.
    /// </summary>
    [Fact]
    public async Task ATokenForADifferentOrg_IsRefusedAndNothingIsWritten() {
        WithConnection(Connection(ConnectionState.Connected, OrgId));

        var ex = await Assert.ThrowsAsync<OrgMismatchException>(
            () => NewSource().RecordTokenSuccessAsync(OrgUrl, "00D000000000002BBB", Ct));

        Assert.Equal(OrgId, ex.StoredOrgId);
        Assert.Equal("00D000000000002BBB", ex.DiscoveredOrgId);
        Assert.Contains("Disconnect", ex.Message);

        await _repository.DidNotReceive().RecordSuccessAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteConnectionAndOrgScopedStateAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A first connection has no stored org id to compare against, so discovery must be allowed to write one.
    /// </summary>
    [Fact]
    public async Task AFirstTokenWithNoStoredOrgId_IsAccepted() {
        WithConnection(Connection());

        await NewSource().RecordTokenSuccessAsync(OrgUrl, OrgId, Ct);

        await _repository.Received(1).RecordSuccessAsync(OrgUrl, OrgId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The org id feeds the Pub/Sub tenant header. "Connected but the worker will not start" is the least
    /// explicable state the application could be in, so a token without one is a failure.
    /// </summary>
    [Fact]
    public async Task ATokenCarryingNoOrgId_IsRecordedAsAFailureRatherThanAPartialSuccess() {
        WithConnection(Connection());

        await NewSource().RecordTokenSuccessAsync(OrgUrl, null, Ct);

        await _repository.DidNotReceive().RecordSuccessAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordFailureAsync(
            Arg.Is<string>(m => m.Contains("org id")), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// Covers reading the org id back out of the token response, which is the application's only source for it.
/// </summary>
public class AuthTokenOrgIdTests {
    [Fact]
    public void TheOrgIdComesFromTheIdentityUrl() {
        var token = new Salesforce.AuthToken {
            Id = "https://login.salesforce.com/id/00D000000000001AAA/005000000000001AAA"
        };

        Assert.Equal("00D000000000001AAA", token.OrgId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://login.salesforce.com/something/else")]
    public void AnIdentityUrlThatIsNotTheExpectedShape_YieldsNoOrgIdRatherThanAGuess(string? id) {
        Assert.Null(new Salesforce.AuthToken { Id = id }.OrgId);
    }
}
