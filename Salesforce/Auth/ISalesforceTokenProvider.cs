namespace Salesforce.Auth;

public interface ISalesforceTokenProvider {
    /// <summary>An access token for one identity, from cache when it is still good.</summary>
    Task<AuthToken> GetAuthToken(SalesforceIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Discards the cached token for one identity and signs a fresh assertion.</summary>
    Task ForceRefreshAsync(SalesforceIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Drops every cached token, so the next request uses whatever credentials are now stored.</summary>
    void ClearCache();
}
