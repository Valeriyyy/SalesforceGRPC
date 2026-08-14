using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Salesforce.Dtos;
using SalesforceGrpc.Salesforce;

namespace Salesforce.Clients;

/// <summary>
/// Manages Salesforce Platform Event channels through the Tooling API.
/// </summary>
/// <remarks>
/// Channels are the PlatformEventChannel object and their members the PlatformEventChannelMember object.
/// Both are metadata-backed: the only createable top-level field is FullName, and everything else travels
/// inside a Metadata envelope. PATCH requires a complete Metadata object, never a partial one.
/// Requires API version 47.0 or later (56.0 for filter expressions, 61.0 for EventType).
/// </remarks>
public sealed class SalesforceToolingClient : BaseSalesforceClient {
    private const string ChannelSObject = "PlatformEventChannel";
    private const string ChannelMemberSObject = "PlatformEventChannelMember";

    /// <summary>Fields safe to select in a multi-record query — FullName and Metadata are excluded.</summary>
    private const string ChannelListFields =
        "Id, MasterLabel, DeveloperName, ChannelType, EventType, Language, ManageableState, NamespacePrefix";

    private const string ChannelMemberListFields =
        "Id, EventChannel, MasterLabel, DeveloperName, FilterExpression, SelectedEntity, ManageableState, NamespacePrefix";

    public SalesforceToolingClient(HttpClient httpClient, ISalesforceTokenProvider tokenProvider,
        IOptions<SalesforceConfig> config, ILogger<SalesforceToolingClient> logger)
        : base(httpClient, config.Value, logger, tokenProvider) {
    }

    #region Channels

    /// <summary>
    /// Lists the org's platform event channels, optionally narrowed to one channel type.
    /// </summary>
    /// <param name="channelType">"data" for Change Data Capture, "event" for platform events, null for all.</param>
    public async Task<List<PlatformEventChannel>> ListChannelsAsync(string? channelType = null,
        CancellationToken cancellationToken = default) {
        var query = $"SELECT {ChannelListFields} FROM {ChannelSObject}";
        if (!string.IsNullOrWhiteSpace(channelType)) {
            query += $" WHERE ChannelType = '{EscapeSoql(channelType)}'";
        }
        query += " ORDER BY DeveloperName";

        var result = await ToolingQueryAsync<PlatformEventChannel>(query, cancellationToken).ConfigureAwait(false);
        return result.Records ?? [];
    }

    /// <summary>
    /// Returns the total number of platform event channels in the org.
    /// </summary>
    public async Task<int> GetChannelCountAsync(CancellationToken cancellationToken = default) {
        var result = await ToolingQueryAsync<PlatformEventChannel>(
            $"SELECT COUNT() FROM {ChannelSObject}", cancellationToken).ConfigureAwait(false);
        return result.TotalSize;
    }

    /// <summary>
    /// Retrieves a single channel, including its Metadata, by Salesforce ID.
    /// </summary>
    public Task<PlatformEventChannel?> GetChannelAsync(string channelId,
        CancellationToken cancellationToken = default) =>
        ToolingGetAsync<PlatformEventChannel>($"sobjects/{ChannelSObject}/{channelId}", cancellationToken);

    /// <summary>
    /// Finds a channel by developer name (the name without the <c>__chn</c> suffix), including its Metadata.
    /// </summary>
    public async Task<PlatformEventChannel?> GetChannelByDeveloperNameAsync(string developerName,
        CancellationToken cancellationToken = default) {
        var result = await ToolingQueryAsync<PlatformEventChannel>(
            $"SELECT Id FROM {ChannelSObject} WHERE DeveloperName = '{EscapeSoql(developerName)}'",
            cancellationToken).ConfigureAwait(false);

        var id = result.Records?.FirstOrDefault()?.Id;
        return id is null ? null : await GetChannelAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a channel and returns its Salesforce ID.
    /// </summary>
    /// <param name="fullName">The channel full name including the <c>__chn</c> suffix, e.g. "SalesEvents__chn".</param>
    /// <param name="label">The display label.</param>
    /// <param name="channelType">"data" for Change Data Capture, "event" for platform events.</param>
    /// <param name="eventType">Optional event type (API 61.0+): "custom", "data" or "monitoring".</param>
    public Task<ToolingSaveResponse> CreateChannelAsync(string fullName, string label, string channelType,
        string? eventType = null, CancellationToken cancellationToken = default) {
        var payload = new {
            FullName = fullName,
            Metadata = new PlatformEventChannelMetadata {
                ChannelType = channelType,
                Label = label,
                EventType = eventType
            }
        };

        _logger.LogInformation("Creating platform event channel {FullName}", fullName);
        return ToolingPostAsync($"sobjects/{ChannelSObject}", payload, cancellationToken);
    }

    /// <summary>
    /// Updates a channel. Salesforce requires the complete Metadata object, so pass every field —
    /// only the label is actually mutable.
    /// </summary>
    public Task UpdateChannelAsync(string channelId, PlatformEventChannelMetadata metadata,
        CancellationToken cancellationToken = default) {
        _logger.LogInformation("Updating platform event channel {ChannelId}", channelId);
        return ToolingPatchAsync($"sobjects/{ChannelSObject}/{channelId}", new { Metadata = metadata }, cancellationToken);
    }

    /// <summary>
    /// Deletes a channel. Salesforce cascades the delete to all of the channel's members.
    /// </summary>
    public Task DeleteChannelAsync(string channelId, CancellationToken cancellationToken = default) {
        _logger.LogInformation("Deleting platform event channel {ChannelId}", channelId);
        return ToolingDeleteAsync($"sobjects/{ChannelSObject}/{channelId}", cancellationToken);
    }

    #endregion

    #region Channel members

    /// <summary>
    /// Lists the members of a channel. Note that EventChannel and SelectedEntity come back as IDs here;
    /// use <see cref="GetChannelMemberAsync"/> for the readable names.
    /// </summary>
    /// <param name="channelId">The channel's Salesforce ID.</param>
    public async Task<List<PlatformEventChannelMember>> ListChannelMembersAsync(string channelId,
        CancellationToken cancellationToken = default) {
        var result = await ToolingQueryAsync<PlatformEventChannelMember>(
            $"SELECT {ChannelMemberListFields} FROM {ChannelMemberSObject} WHERE EventChannel = '{EscapeSoql(channelId)}'",
            cancellationToken).ConfigureAwait(false);
        return result.Records ?? [];
    }

    /// <summary>
    /// Retrieves a single channel member, including its Metadata, by Salesforce ID.
    /// </summary>
    public Task<PlatformEventChannelMember?> GetChannelMemberAsync(string memberId,
        CancellationToken cancellationToken = default) =>
        ToolingGetAsync<PlatformEventChannelMember>($"sobjects/{ChannelMemberSObject}/{memberId}", cancellationToken);

    /// <summary>
    /// Adds an event/entity to a channel and returns the new member's Salesforce ID.
    /// </summary>
    public Task<ToolingSaveResponse> CreateChannelMemberAsync(string fullName,
        PlatformEventChannelMemberMetadata metadata, CancellationToken cancellationToken = default) {
        var payload = new {
            FullName = fullName,
            Metadata = metadata
        };

        _logger.LogInformation("Creating channel member {FullName} for entity {SelectedEntity}",
            fullName, metadata.SelectedEntity);
        return ToolingPostAsync($"sobjects/{ChannelMemberSObject}", payload, cancellationToken);
    }

    /// <summary>
    /// Updates a channel member. Only filterExpression and enrichedFields are mutable, but Salesforce
    /// requires the complete Metadata object including the immutable eventChannel and selectedEntity.
    /// Supplied enriched fields replace the existing set.
    /// </summary>
    public Task UpdateChannelMemberAsync(string memberId, string fullName,
        PlatformEventChannelMemberMetadata metadata, CancellationToken cancellationToken = default) {
        var payload = new {
            FullName = fullName,
            Metadata = metadata
        };

        _logger.LogInformation("Updating channel member {MemberId}", memberId);
        return ToolingPatchAsync($"sobjects/{ChannelMemberSObject}/{memberId}", payload, cancellationToken);
    }

    /// <summary>
    /// Removes an event/entity from a channel.
    /// </summary>
    public Task DeleteChannelMemberAsync(string memberId, CancellationToken cancellationToken = default) {
        _logger.LogInformation("Deleting channel member {MemberId}", memberId);
        return ToolingDeleteAsync($"sobjects/{ChannelMemberSObject}/{memberId}", cancellationToken);
    }

    /// <summary>
    /// Builds a channel member's FullName from its channel and entity.
    /// </summary>
    /// <remarks>
    /// The member name is <c>&lt;channel&gt;_&lt;entity&gt;</c> with every double underscore flattened to a
    /// single one, because consecutive underscores are reserved for namespace prefixes and component
    /// suffixes. "SalesEvents__chn" + "AccountChangeEvent" becomes "SalesEvents_chn_AccountChangeEvent",
    /// and "Order_Channel__chn" + "Order_NorthAmer__e" becomes "Order_Channel_chn_Order_NorthAmer_e".
    /// Note that Metadata.eventChannel keeps its double underscores — only FullName is flattened.
    /// </remarks>
    public static string BuildMemberFullName(string channelFullName, string selectedEntity) {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedEntity);
        return Flatten($"{channelFullName}_{selectedEntity}");
    }

    private static string Flatten(string value) {
        while (value.Contains("__", StringComparison.Ordinal)) {
            value = value.Replace("__", "_", StringComparison.Ordinal);
        }
        return value;
    }

    #endregion

    #region Discovery

    /// <summary>
    /// Returns the entities that may be added to a channel, read from the SelectedEntity picklist on the
    /// PlatformEventChannelMember describe. This picklist is the authoritative list — it reflects what
    /// Salesforce will actually accept, unlike inferring from EntityDefinition.
    /// </summary>
    /// <param name="channelType">
    /// "data" to return only Change Data Capture entities (names ending in ChangeEvent), "event" to return
    /// only platform events, or null for everything.
    /// </param>
    public async Task<List<ToolingPicklistValue>> GetSelectableEntitiesAsync(string? channelType = null,
        CancellationToken cancellationToken = default) {
        var describe = await ToolingGetAsync<ToolingDescribeResponse>(
            $"sobjects/{ChannelMemberSObject}/describe", cancellationToken).ConfigureAwait(false);

        var values = describe?.Fields?
            .FirstOrDefault(f => string.Equals(f.Name, "SelectedEntity", StringComparison.OrdinalIgnoreCase))?
            .PicklistValues?
            .Where(p => p.Active && !string.IsNullOrWhiteSpace(p.Value))
            // The picklist can carry orphaned entries holding a raw record ID instead of an API name
            // (seen in practice for entities that no longer resolve). An API name can never start with a
            // digit, so such entries are unusable as selectedEntity and are dropped.
            .Where(p => !char.IsDigit(p.Value![0]))
            .ToList() ?? [];

        // Salesforce leaves these picklist labels empty, so fall back to the API name for display.
        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value.Label)) {
                value.Label = value.Value;
            }
        }

        if (string.Equals(channelType, "data", StringComparison.OrdinalIgnoreCase)) {
            values = values.Where(p => p.Value!.EndsWith("ChangeEvent", StringComparison.Ordinal)).ToList();
        } else if (string.Equals(channelType, "event", StringComparison.OrdinalIgnoreCase)) {
            values = values.Where(p => !p.Value!.EndsWith("ChangeEvent", StringComparison.Ordinal)).ToList();
        }

        return values.OrderBy(p => p.Value, StringComparer.Ordinal).ToList();
    }

    #endregion
}
