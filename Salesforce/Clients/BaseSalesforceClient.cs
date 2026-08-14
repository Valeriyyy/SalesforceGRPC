using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Salesforce.Dtos;
using SalesforceGrpc.Salesforce;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Salesforce.Clients;

public class BaseSalesforceClient {
    protected readonly SalesforceConfig _config;
    protected readonly ILogger<BaseSalesforceClient> _logger;
    protected readonly ISalesforceTokenProvider _tokenProvider;
    protected readonly HttpClient _client;

    private static readonly JsonSerializerSettings SerializerSettings = new() {
        NullValueHandling = NullValueHandling.Ignore
    };

    protected BaseSalesforceClient(HttpClient client, SalesforceConfig configuration, ILogger<BaseSalesforceClient> logger, ISalesforceTokenProvider tokenProvider) {
        _client = client;
        _config = configuration;
        _logger = logger;
        _tokenProvider = tokenProvider;
    }

    /// <summary>
    /// Forces the shared client's default Authorization header to a valid token.
    /// </summary>
    /// <remarks>
    /// Prefer letting <c>SalesforceAuthHandler</c> apply the token per request — it is registered on
    /// every typed client and already refreshes on a 401. This method mutates
    /// <see cref="HttpClient.DefaultRequestHeaders"/> on an instance shared across concurrent
    /// requests, so it is not safe to call from parallel code paths.
    /// </remarks>
    protected async Task EnsureValidTokenAsync(CancellationToken cancellationToken = default) {
        try {
            var token = await _tokenProvider.GetAuthToken(cancellationToken);
            if (string.IsNullOrEmpty(token?.AccessToken)) {
                await _tokenProvider.ForceRefreshAsync(cancellationToken);
                token = await _tokenProvider.GetAuthToken(cancellationToken);
            }
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token?.AccessToken);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to ensure a valid Salesforce access token");
            throw;
        }
    }

    /// <summary>
    /// Builds an absolute Tooling API URL, e.g. <c>sobjects/PlatformEventChannel/0YL...</c>.
    /// </summary>
    protected string ToolingUrl(string relativePath) =>
        $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/{relativePath}";

    /// <summary>
    /// Escapes a value for safe interpolation into a single-quoted SOQL string literal.
    /// </summary>
    protected static string EscapeSoql(string value) {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    /// <summary>
    /// Runs a SOQL query against the Tooling API.
    /// </summary>
    protected async Task<ToolingQueryResponse<T>> ToolingQueryAsync<T>(string soql, CancellationToken cancellationToken = default) {
        var url = ToolingUrl($"query?q={WebUtility.UrlEncode(soql)}");
        _logger.LogDebug("Tooling query: {Soql}", soql);

        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var content = await ReadOrThrowAsync(response, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);

        return JsonConvert.DeserializeObject<ToolingQueryResponse<T>>(content) ?? new ToolingQueryResponse<T>();
    }

    /// <summary>
    /// Retrieves a single record (or a describe result) from the Tooling API.
    /// </summary>
    protected async Task<T?> ToolingGetAsync<T>(string relativePath, CancellationToken cancellationToken = default) {
        var url = ToolingUrl(relativePath);

        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) {
            return default;
        }
        var content = await ReadOrThrowAsync(response, HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);

        return JsonConvert.DeserializeObject<T>(content);
    }

    /// <summary>
    /// Creates a record via the Tooling API.
    /// </summary>
    protected async Task<ToolingSaveResponse> ToolingPostAsync(string relativePath, object body, CancellationToken cancellationToken = default) {
        var url = ToolingUrl(relativePath);
        using var content = Serialize(body);

        using var response = await _client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var responseBody = await ReadOrThrowAsync(response, HttpMethod.Post, url, cancellationToken).ConfigureAwait(false);

        var result = JsonConvert.DeserializeObject<ToolingSaveResponse>(responseBody)
                     ?? throw new SalesforceToolingException(response.StatusCode, [], responseBody);

        // Salesforce can answer 2xx with success:false and a populated errors array.
        if (!result.Success) {
            throw new SalesforceToolingException(response.StatusCode, result.Errors ?? [], responseBody);
        }
        return result;
    }

    /// <summary>
    /// Updates a record via the Tooling API. Returns no content on success.
    /// </summary>
    protected async Task ToolingPatchAsync(string relativePath, object body, CancellationToken cancellationToken = default) {
        var url = ToolingUrl(relativePath);
        using var content = Serialize(body);

        using var response = await _client.PatchAsync(url, content, cancellationToken).ConfigureAwait(false);
        await ReadOrThrowAsync(response, HttpMethod.Patch, url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a record via the Tooling API.
    /// </summary>
    protected async Task ToolingDeleteAsync(string relativePath, CancellationToken cancellationToken = default) {
        var url = ToolingUrl(relativePath);

        using var response = await _client.DeleteAsync(url, cancellationToken).ConfigureAwait(false);
        await ReadOrThrowAsync(response, HttpMethod.Delete, url, cancellationToken).ConfigureAwait(false);
    }

    private static StringContent Serialize(object body) =>
        new(JsonConvert.SerializeObject(body, SerializerSettings), Encoding.UTF8, "application/json");

    /// <summary>
    /// Returns the response body on success; otherwise logs and throws a
    /// <see cref="SalesforceToolingException"/> carrying the parsed Salesforce errors.
    /// </summary>
    private async Task<string> ReadOrThrowAsync(HttpResponseMessage response, HttpMethod method, string url, CancellationToken cancellationToken) {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) {
            return content;
        }

        var errors = ParseErrors(content);
        _logger.LogError("Tooling API {Method} {Url} failed with {StatusCode}: {Body}",
            method, url, (int)response.StatusCode, content);

        throw new SalesforceToolingException(response.StatusCode, errors, content);
    }

    /// <summary>
    /// Tooling API errors normally arrive as an array; a few endpoints return a single object.
    /// </summary>
    private static IReadOnlyList<ToolingError> ParseErrors(string content) {
        if (string.IsNullOrWhiteSpace(content)) {
            return [];
        }
        try {
            return JsonConvert.DeserializeObject<List<ToolingError>>(content) ?? [];
        } catch (JsonException) {
            try {
                var single = JsonConvert.DeserializeObject<ToolingError>(content);
                return single is null ? [] : [single];
            } catch (JsonException) {
                return [];
            }
        }
    }
}
