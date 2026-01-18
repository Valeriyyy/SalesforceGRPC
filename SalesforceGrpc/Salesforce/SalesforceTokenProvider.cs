using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace SalesforceGrpc.Salesforce;

public interface ISalesforceTokenProvider {
    Task<AuthToken> GetAuthToken(CancellationToken cancellationToken = default);
    Task ForceRefreshAsync(CancellationToken cancellationToken = default);
}

public class SalesforceTokenProvider : ISalesforceTokenProvider {
    private readonly HttpClient _authClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ILogger<SalesforceTokenProvider> _logger;
    private readonly SalesforceConfig _config;
    
    private AuthToken? _accessToken;
    private DateTimeOffset _issuedAt;
    private readonly TimeSpan _tokenLifetime = TimeSpan.FromMinutes(30);

    public SalesforceTokenProvider(HttpClient authClient, IOptions<SalesforceConfig> configurationOptions, ILogger<SalesforceTokenProvider> logger) {
        _authClient = authClient;
        _logger = logger;
        _config = configurationOptions.Value;
    }

    public async Task<AuthToken> GetAuthToken(CancellationToken cancellationToken = default) {
        // Optional proactive refresh window (e.g. 50 minutes)
        if (_accessToken != null && DateTimeOffset.UtcNow - _issuedAt < _tokenLifetime)
            return _accessToken;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check inside lock
            if (_accessToken != null && DateTimeOffset.UtcNow - _issuedAt < _tokenLifetime)
                return _accessToken;

            var response = await RequestNewTokenAsync(cancellationToken);
            _accessToken = response;
            _issuedAt = DateTimeOffset.UtcNow;

            return _accessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
    
    public async Task ForceRefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            await RequestNewTokenAsync(ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<AuthToken> RequestNewTokenAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            _config.LoginUrl);

        var nvc = new List<KeyValuePair<string, string>>
        {
            new("grant_type", _config.GrantType),
            new("client_id", _config.ClientId),
            new("client_secret", _config.ClientSecret),
            new("username", _config.Username),
            new("password", _config.Password + _config.UserSecurityToken)
            //new KeyValuePair<string, string>("password", configuration.Password)
        };
        request.Content = new FormUrlEncodedContent(nvc);

        var response = await _authClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var authToken = JsonConvert.DeserializeObject<AuthToken>(content);
        
        _accessToken = authToken?.AccessToken != null ? authToken : null;
        _issuedAt = DateTimeOffset.UtcNow;

        return authToken
               ?? throw new InvalidOperationException("Invalid auth response");
    }
}