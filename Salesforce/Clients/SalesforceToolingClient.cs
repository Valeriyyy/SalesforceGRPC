using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salesforce.Dtos;
using SalesforceGrpc.Salesforce;

namespace Salesforce.Clients;

public class SalesforceToolingClient {
    private readonly HttpClient _client;
    private readonly ISalesforceTokenProvider _tokenProvider;
    private readonly SalesforceConfig _config;
    private readonly ILogger<SalesforceToolingClient> _logger;

    public SalesforceToolingClient(HttpClient httpClient, ISalesforceTokenProvider tokenProvider, 
        IOptions<SalesforceConfig> configurationOptions, ILogger<SalesforceToolingClient> logger) {
        _client = httpClient;
        _tokenProvider = tokenProvider;
        _config = configurationOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ensures a valid Salesforce API token is available and sets the Authorization header.
    /// </summary>
    private async Task EnsureValidTokenAsync(CancellationToken cancellationToken = default) {
        try {
            var token = await _tokenProvider.GetAuthToken(cancellationToken);
            if (string.IsNullOrEmpty(token?.AccessToken)) {
                await _tokenProvider.ForceRefreshAsync(cancellationToken);
                token = await _tokenProvider.GetAuthToken(cancellationToken);
            }
            _client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to ensure valid token for Tooling API");
            throw;
        }
    }

    /// <summary>
    /// Retrieves all CDC channels for the organization.
    /// </summary>
    public async Task<List<PlatformEventChannel>> GetCDCChannelsAsync(CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var query = "SELECT Id, MasterLabel, DeveloperName, ChannelType, EventType, FullName, Language, ManageableState, Metadata, NamespacePrefix FROM PlatformEventChannel LIMIT 1 offset 1";
            var encodedQuery = System.Net.WebUtility.UrlEncode(query);
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/query?q={encodedQuery}";
            
            _logger.LogInformation("Fetching CDC channels from {Endpoint}", endpoint);
            
            var response = await _client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<ToolingQueryResponse<PlatformEventChannel>>(content);
            
            return result?.Records ?? new List<PlatformEventChannel>();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving CDC channels");
            throw;
        }
    }

    /// <summary>
    /// Retrieves a specific CDC channel by its developer name.
    /// </summary>
    public async Task<PlatformEventChannel?> GetCDCChannelByDeveloperNameAsync(string developerName, 
        CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var query = $"SELECT Id, MasterLabel, DeveloperName, ChannelType, EventType, FullName, Language, ManageableState, Metadata, NamespacePrefix FROM PlatformEventChannel WHERE DeveloperName = '{developerName}'";
            var encodedQuery = System.Net.WebUtility.UrlEncode(query);
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/query?q={encodedQuery}";
            
            _logger.LogInformation("Fetching CDC channel {DeveloperName}", developerName);
            
            var response = await _client.GetAsync(endpoint, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ToolingQueryResponse<PlatformEventChannel>>(content);
            
            return result?.Records?.FirstOrDefault();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving CDC channel {DeveloperName}", developerName);
            throw;
        }
    }

    /// <summary>
    /// Creates a new CDC channel.
    /// </summary>
    public async Task<CDCChannelCreateResponse> CreateCDCChannelAsync(string masterLabel, string developerName,
        string description = "", CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/sobjects/EventChannel";
            
            var payload = new {
                MasterLabel = masterLabel,
                DeveloperName = developerName,
                EventChannelType = "Change Data Capture",
                Description = description
            };
            
            var content = new StringContent(JsonConvert.SerializeObject(payload), 
                System.Text.Encoding.UTF8, "application/json");
            
            _logger.LogInformation("Creating CDC channel {DeveloperName}", developerName);
            
            var response = await _client.PostAsync(endpoint, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonConvert.DeserializeObject<CDCChannelCreateResponse>(responseContent) 
                   ?? throw new InvalidOperationException("Invalid create response");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error creating CDC channel {DeveloperName}", developerName);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing CDC channel.
    /// </summary>
    public async Task<bool> UpdateCDCChannelAsync(string channelId, string? masterLabel = null,
        string? description = null, CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/sobjects/EventChannel/{channelId}";
            
            var payload = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(masterLabel))
                payload["MasterLabel"] = masterLabel;
            if (!string.IsNullOrEmpty(description))
                payload["Description"] = description;
            
            var content = new StringContent(JsonConvert.SerializeObject(payload),
                System.Text.Encoding.UTF8, "application/json");
            
            _logger.LogInformation("Updating CDC channel {ChannelId}", channelId);
            
            var request = new HttpRequestMessage(HttpMethod.Patch, endpoint) {
                Content = content
            };
            
            var response = await _client.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error updating CDC channel {ChannelId}", channelId);
            throw;
        }
    }

    /// <summary>
    /// Deletes a CDC channel by its ID.
    /// </summary>
    public async Task<bool> DeleteCDCChannelAsync(string channelId, CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/sobjects/EventChannel/{channelId}";
            
            _logger.LogInformation("Deleting CDC channel {ChannelId}", channelId);
            
            var response = await _client.DeleteAsync(endpoint, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error deleting CDC channel {ChannelId}", channelId);
            throw;
        }
    }

    public async Task<List<PlatformEventChannelMember>> GetPlatformChannelEventMembers(string eventChannel, CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var query = $"SELECT Id, EventChannel, MasterLabel, DeveloperName, FilterExpression, SelectedEntity FROM PlatformEventChannelMember where EventChannel = '{eventChannel}'";
            var encodedQuery = System.Net.WebUtility.UrlEncode(query);
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/query?q={encodedQuery}";
            
            _logger.LogInformation("Fetching CDC channels from {Endpoint}", endpoint);
            
            var response = await _client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<ToolingQueryResponse<PlatformEventChannelMember>>(content);
            
            return result?.Records ?? new List<PlatformEventChannelMember>();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving CDC channels");
            throw;
        }
    }

    /// <summary>
    /// Retrieves enriched fields for a CDC channel.
    /// </summary>
    public async Task<List<EnrichedField>> GetEnrichedFieldsAsync(string channelId, 
        CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var query = $"SELECT Id, EventChannelId, EntityId, EntityFieldId, IsSelected FROM EventChannelMember WHERE EventChannelId = '{channelId}' ORDER BY EntityId, EntityFieldId";
            var encodedQuery = System.Net.WebUtility.UrlEncode(query);
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/query?q={encodedQuery}";
            
            _logger.LogInformation("Fetching enriched fields for channel {ChannelId}", channelId);
            
            var response = await _client.GetAsync(endpoint, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ToolingQueryResponse<EnrichedField>>(content);
            
            return result?.Records ?? new List<EnrichedField>();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving enriched fields for channel {ChannelId}", channelId);
            throw;
        }
    }

    /// <summary>
    /// Adds enriched fields to a CDC channel.
    /// </summary>
    public async Task<EnrichedFieldCreateResponse> AddEnrichedFieldAsync(string channelId, string entityId,
        List<string> fieldIds, CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/sobjects/EventChannelMember";
            
            var payload = new {
                EventChannelId = channelId,
                EntityId = entityId,
                SelectedEntityFields = string.Join(",", fieldIds)
            };
            
            var content = new StringContent(JsonConvert.SerializeObject(payload),
                System.Text.Encoding.UTF8, "application/json");
            
            _logger.LogInformation("Adding enriched fields to channel {ChannelId} for entity {EntityId}", 
                channelId, entityId);
            
            var response = await _client.PostAsync(endpoint, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonConvert.DeserializeObject<EnrichedFieldCreateResponse>(responseContent)
                   ?? throw new InvalidOperationException("Invalid create response");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error adding enriched fields to channel {ChannelId}", channelId);
            throw;
        }
    }

    /// <summary>
    /// Updates enriched fields for a CDC channel member.
    /// </summary>
    public async Task<bool> UpdateEnrichedFieldsAsync(string memberId, List<string> fieldIds,
        CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/sobjects/EventChannelMember/{memberId}";
            
            var payload = new {
                SelectedEntityFields = string.Join(",", fieldIds)
            };
            
            var content = new StringContent(JsonConvert.SerializeObject(payload),
                System.Text.Encoding.UTF8, "application/json");
            
            _logger.LogInformation("Updating enriched fields for member {MemberId}", memberId);
            
            var request = new HttpRequestMessage(HttpMethod.Patch, endpoint) {
                Content = content
            };
            
            var response = await _client.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error updating enriched fields for member {MemberId}", memberId);
            throw;
        }
    }

    /// <summary>
    /// Removes enriched fields from a CDC channel member.
    /// </summary>
    public async Task<bool> RemoveEnrichedFieldAsync(string memberId, CancellationToken cancellationToken = default) {
        await EnsureValidTokenAsync(cancellationToken);
        
        try {
            var endpoint = $"{_config.OrgUrl}/services/data/v{_config.ApiVersion}/tooling/sobjects/EventChannelMember/{memberId}";
            
            _logger.LogInformation("Removing enriched fields for member {MemberId}", memberId);
            
            var response = await _client.DeleteAsync(endpoint, cancellationToken);
            
            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Tooling API Error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
            
            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error removing enriched fields for member {MemberId}", memberId);
            throw;
        }
    }
}
