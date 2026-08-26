using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Salesforce.Auth;

/// <summary>
/// Thrown when a token is requested but no Org Connection has been set up.
/// </summary>
/// <remarks>
/// Distinct from a rejected token: nothing has been tried, and the remedy is setup rather than diagnosis.
/// </remarks>
public sealed class NoOrgConnectionException : InvalidOperationException {
    public NoOrgConnectionException()
        : base("No Salesforce Org Connection has been set up, so no access token can be obtained. " +
               "Configure one through the API.") { }
}

public interface ISalesforceTokenProvider {
    /// <summary>An access token for one identity, from cache when it is still good.</summary>
    Task<AuthToken> GetAuthToken(SalesforceIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Discards the cached token for one identity and signs a fresh assertion.</summary>
    Task ForceRefreshAsync(SalesforceIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Drops every cached token, so the next request uses whatever credentials are now stored.</summary>
    void ClearCache();
}

/// <summary>
/// Obtains Salesforce access tokens through the OAuth 2.0 JWT Bearer flow.
/// </summary>
/// <remarks>
/// Replaces a username-password flow that POSTed a client secret, password and security token. Nothing is
/// sent now but a assertion signed with a key this application generated for itself, which is what lets it
/// reconnect after a restart with nobody present.
/// <para>
/// Tokens are cached per identity, not globally. The <c>sub</c> claim names a user, so the Administering User
/// and the Run-as User get different tokens from the same External Client App and the same keypair — sharing
/// one cache entry between them would hand the event stream an administrator's token.
/// </para>
/// <para>
/// The Org Connection is read through <see cref="IOrgConnectionSource"/> on every refresh rather than captured
/// at construction, so an edited connection takes effect without a restart.
/// </para>
/// </remarks>
public sealed class SalesforceTokenProvider : ISalesforceTokenProvider {
    /// <summary>The named HttpClient used for token requests. Registered in the composition root.</summary>
    public const string HttpClientName = "SalesforceTokenEndpoint";

    /// <summary>
    /// How long a token is assumed good when Salesforce does not say.
    /// </summary>
    /// <remarks>
    /// Salesforce's session length is an org setting and the JWT Bearer response does not carry
    /// <c>expires_in</c>, so some assumption is unavoidable. This is well under the shortest session an org
    /// can be configured with; the cost of being wrong is a redundant token request, and a fresh assertion
    /// costs one signature.
    /// </remarks>
    private static readonly TimeSpan AssumedTokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Refresh this far before expiry, so a request in flight does not cross the boundary.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOrgConnectionSource _connections;
    private readonly ILogger<SalesforceTokenProvider> _logger;
    private readonly TimeProvider _time;

    private readonly ConcurrentDictionary<SalesforceIdentity, CachedToken> _tokens = new();
    private readonly ConcurrentDictionary<SalesforceIdentity, SemaphoreSlim> _refreshLocks = new();

    public SalesforceTokenProvider(IHttpClientFactory httpClientFactory, IOrgConnectionSource connections,
        ILogger<SalesforceTokenProvider> logger, TimeProvider time) {
        _httpClientFactory = httpClientFactory;
        _connections = connections;
        _logger = logger;
        _time = time;
    }

    public async Task<AuthToken> GetAuthToken(SalesforceIdentity identity, CancellationToken cancellationToken = default) {
        if (TryGetCached(identity, out var cached)) {
            return cached;
        }

        var refreshLock = _refreshLocks.GetOrAdd(identity, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            // Another caller may have refreshed while this one waited.
            if (TryGetCached(identity, out cached)) {
                return cached;
            }

            return await RequestTokenAsync(identity, cancellationToken).ConfigureAwait(false);
        } finally {
            refreshLock.Release();
        }
    }

    public async Task ForceRefreshAsync(SalesforceIdentity identity, CancellationToken cancellationToken = default) {
        var refreshLock = _refreshLocks.GetOrAdd(identity, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            _tokens.TryRemove(identity, out _);
            await RequestTokenAsync(identity, cancellationToken).ConfigureAwait(false);
        } finally {
            refreshLock.Release();
        }
    }

    public void ClearCache() {
        _tokens.Clear();
        _logger.LogDebug("Cleared cached Salesforce access tokens");
    }

    private bool TryGetCached(SalesforceIdentity identity, out AuthToken token) {
        token = null!;

        if (!_tokens.TryGetValue(identity, out var cached) || cached.ExpiresAt <= _time.GetUtcNow()) {
            return false;
        }

        token = cached.Token;
        return true;
    }

    private async Task<AuthToken> RequestTokenAsync(SalesforceIdentity identity, CancellationToken cancellationToken) {
        var connection = await _connections.GetConnectionAsync(cancellationToken).ConfigureAwait(false)
                         ?? throw new NoOrgConnectionException();

        var now = _time.GetUtcNow();
        var assertion = JwtAssertionFactory.Create(connection, identity, now);

        using var client = _httpClientFactory.CreateClient(HttpClientName);

        AuthToken token;
        try {
            token = await SalesforceTokenEndpoint.PostAsync(client, connection.LoginHost, [
                new KeyValuePair<string, string>("grant_type", JwtAssertionFactory.GrantType),
                new KeyValuePair<string, string>("assertion", assertion)
            ], connection.CertificateFingerprint, cancellationToken).ConfigureAwait(false);
        } catch (SalesforceOAuthException exc) {
            // The whole error, so the raw Salesforce text survives as far as the API. A user reading a
            // translated guess with no way back to what Salesforce actually said is worse off than one
            // reading the raw text alone.
            await _connections.RecordTokenFailureAsync(exc.Error, cancellationToken).ConfigureAwait(false);
            throw;
        }

        // Recorded before the token is cached, not after. This call is where an org change is caught and
        // refused, and a token cached first would stay live in the cache — and get used — for an org this
        // application is not connected to.
        await _connections.RecordTokenSuccessAsync(token.InstanceUrl!, token.OrgId, cancellationToken)
            .ConfigureAwait(false);

        var lifetime = token.ExpiresInSeconds is > 0
            ? TimeSpan.FromSeconds(token.ExpiresInSeconds.Value)
            : AssumedTokenLifetime;

        _tokens[identity] = new CachedToken(token, now.Add(lifetime) - RefreshMargin);

        _logger.LogInformation("Obtained a Salesforce access token for the {Identity} ({Username})",
            identity, connection.UsernameFor(identity));

        return token;
    }

    private sealed record CachedToken(AuthToken Token, DateTimeOffset ExpiresAt);
}
