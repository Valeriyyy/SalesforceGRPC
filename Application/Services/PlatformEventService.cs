using Application.Services.Interfaces;
using Database.Models;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Salesforce.Clients;
using Salesforce.Dtos;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Application.Services;

/// <summary>
/// Creates and manages Salesforce platform event channels through the Tooling API, mirroring the result
/// into the app database.
/// </summary>
/// <remarks>
/// Every mutating operation validates first, then calls Salesforce, then retrieves the saved record, then
/// upserts the mirror. The retrieve is not optional: SOQL returns IDs where the readable channel and
/// entity names are wanted, and Salesforce derives DeveloperName itself.
/// </remarks>
public class PlatformEventService : IPlatformEventService {
    private const string ChannelSuffix = "__chn";
    private const string ChangeEventSuffix = "ChangeEvent";
    private const string ChannelTypeData = "data";
    private const string ChannelTypeEvent = "event";

    private static readonly string[] ValidChannelTypes = [ChannelTypeData, ChannelTypeEvent];
    private static readonly string[] ValidEventTypes = ["custom", "data", "monitoring"];

    /// <summary>
    /// A developer name: starts with a letter, alphanumeric and single underscores, no trailing underscore.
    /// </summary>
    private static readonly Regex DeveloperNamePattern =
        new(@"^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$", RegexOptions.Compiled);

    private readonly SalesforceToolingClient _toolingClient;
    private readonly IPlatformEventChannelRepository _repo;
    private readonly ILogger<PlatformEventService> _logger;

    public PlatformEventService(SalesforceToolingClient toolingClient,
        IPlatformEventChannelRepository repo, ILogger<PlatformEventService> logger) {
        _toolingClient = toolingClient;
        _repo = repo;
        _logger = logger;
    }

    #region Channels

    public Task<List<PlatformEventChannelEntity>> GetChannelsAsync(CancellationToken cancellationToken = default) =>
        _repo.GetChannelsAsync(cancellationToken);

    public Task<PlatformEventChannelEntity?> GetChannelAsync(int id, CancellationToken cancellationToken = default) =>
        _repo.GetChannelByIdAsync(id, cancellationToken);

    /// <summary>
    /// Creates a channel in Salesforce and mirrors it locally.
    /// </summary>
    public async Task<PlatformEventChannelEntity> CreateChannelAsync(CreateChannelDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var fullName = request.FullName?.Trim() ?? string.Empty;
        ValidateChannelFullName(fullName);

        if (string.IsNullOrWhiteSpace(request.Label)) {
            throw new ValidationException("Label is required.");
        }

        var channelType = NormalizeChannelType(request.ChannelType);
        var eventType = NormalizeEventType(request.EventType);

        var saveResult = await _toolingClient
            .CreateChannelAsync(fullName, request.Label.Trim(), channelType, eventType, cancellationToken)
            .ConfigureAwait(false);

        var created = await _toolingClient.GetChannelAsync(saveResult.Id!, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(
                          $"Channel {saveResult.Id} was created but could not be read back from Salesforce.");

        return await MirrorChannelAsync(created, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a channel's label. Salesforce requires the complete Metadata object on PATCH, so the
    /// immutable fields are read from the existing record and sent back unchanged.
    /// </summary>
    public async Task<PlatformEventChannelEntity> UpdateChannelAsync(int id, UpdateChannelDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await RequireChannelAsync(id, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.Label)) {
            throw new ValidationException("Label is required.");
        }

        // Salesforce fixes these at create time, so a mismatched value would be silently dropped.
        if (request.ChannelType is not null &&
            !string.Equals(NormalizeChannelType(request.ChannelType), existing.ChannelType, StringComparison.OrdinalIgnoreCase)) {
            throw new ValidationException(
                $"ChannelType cannot be changed after the channel is created (it is '{existing.ChannelType}').");
        }
        if (request.EventType is not null &&
            !string.Equals(NormalizeEventType(request.EventType), existing.EventType, StringComparison.OrdinalIgnoreCase)) {
            throw new ValidationException(
                $"EventType cannot be changed after the channel is created (it is '{existing.EventType ?? "unset"}').");
        }

        var metadata = new PlatformEventChannelMetadata {
            ChannelType = existing.ChannelType,
            Label = request.Label.Trim(),
            EventType = existing.EventType
        };

        await _toolingClient.UpdateChannelAsync(existing.SfId, metadata, cancellationToken).ConfigureAwait(false);

        var updated = await _toolingClient.GetChannelAsync(existing.SfId, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(
                          $"Channel {existing.SfId} was updated but could not be read back from Salesforce.");

        return await MirrorChannelAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a channel in Salesforce, then removes the local mirror. Members are deleted first so a
    /// failure names the member that blocked it, though Salesforce would also cascade them.
    /// </summary>
    public async Task DeleteChannelAsync(int id, CancellationToken cancellationToken = default) {
        var existing = await RequireChannelAsync(id, cancellationToken).ConfigureAwait(false);

        foreach (var member in existing.Members) {
            await _toolingClient.DeleteChannelMemberAsync(member.SfId, cancellationToken).ConfigureAwait(false);
        }

        await _toolingClient.DeleteChannelAsync(existing.SfId, cancellationToken).ConfigureAwait(false);
        await _repo.DeleteChannelAsync(existing.Id, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Members

    public async Task<List<PlatformEventChannelMemberEntity>> GetChannelMembersAsync(int channelId, CancellationToken cancellationToken = default) {
        await RequireChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        return await _repo.GetMembersByChannelIdAsync(channelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds an event/entity to a channel in Salesforce and mirrors the member locally.
    /// </summary>
    public async Task<PlatformEventChannelMemberEntity> AddChannelMemberAsync(int channelId, CreateChannelMemberDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var channel = await RequireChannelAsync(channelId, cancellationToken).ConfigureAwait(false);

        var selectedEntity = request.SelectedEntity?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selectedEntity)) {
            throw new ValidationException("SelectedEntity is required.");
        }
        ValidateEntityMatchesChannelType(selectedEntity, channel.ChannelType);

        var metadata = new PlatformEventChannelMemberMetadata {
            EventChannel = channel.FullName,
            SelectedEntity = selectedEntity,
            FilterExpression = string.IsNullOrWhiteSpace(request.FilterExpression) ? null : request.FilterExpression.Trim(),
            EnrichedFields = ToEnrichedFields(request.EnrichedFields)
        };

        var fullName = SalesforceToolingClient.BuildMemberFullName(channel.FullName, selectedEntity);

        var saveResult = await _toolingClient
            .CreateChannelMemberAsync(fullName, metadata, cancellationToken).ConfigureAwait(false);

        var created = await _toolingClient.GetChannelMemberAsync(saveResult.Id!, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(
                          $"Channel member {saveResult.Id} was created but could not be read back from Salesforce.");

        return await _repo.UpsertMemberAsync(MapMember(created, channel.Id, fullName), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a member's filter expression and enriched fields. Both replace rather than merge, matching
    /// how Salesforce treats a PATCH.
    /// </summary>
    public async Task<PlatformEventChannelMemberEntity> UpdateChannelMemberAsync(int memberId, UpdateChannelMemberDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await RequireMemberAsync(memberId, cancellationToken).ConfigureAwait(false);
        var channel = await RequireChannelAsync(existing.ChannelId, cancellationToken).ConfigureAwait(false);

        // Salesforce fixes the entity at create time, so a mismatched value would be silently dropped.
        if (request.SelectedEntity is not null &&
            !string.Equals(request.SelectedEntity.Trim(), existing.SelectedEntity, StringComparison.OrdinalIgnoreCase)) {
            throw new ValidationException(
                $"SelectedEntity cannot be changed after the member is created (it is '{existing.SelectedEntity}'). " +
                "Remove this member and add a new one instead.");
        }

        var metadata = new PlatformEventChannelMemberMetadata {
            EventChannel = channel.FullName,
            SelectedEntity = existing.SelectedEntity,
            FilterExpression = string.IsNullOrWhiteSpace(request.FilterExpression) ? null : request.FilterExpression.Trim(),
            EnrichedFields = ToEnrichedFields(request.EnrichedFields)
        };

        await _toolingClient
            .UpdateChannelMemberAsync(existing.SfId, existing.FullName, metadata, cancellationToken)
            .ConfigureAwait(false);

        var updated = await _toolingClient.GetChannelMemberAsync(existing.SfId, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(
                          $"Channel member {existing.SfId} was updated but could not be read back from Salesforce.");

        return await _repo.UpsertMemberAsync(MapMember(updated, channel.Id, existing.FullName), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes an event/entity from a channel in Salesforce, then drops the local mirror row.
    /// </summary>
    public async Task RemoveChannelMemberAsync(int memberId, CancellationToken cancellationToken = default) {
        var existing = await RequireMemberAsync(memberId, cancellationToken).ConfigureAwait(false);

        await _toolingClient.DeleteChannelMemberAsync(existing.SfId, cancellationToken).ConfigureAwait(false);
        await _repo.DeleteMemberBySfIdAsync(existing.SfId, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Discovery and sync

    public Task<List<ToolingPicklistValue>> GetSelectableEntitiesAsync(string? channelType = null, CancellationToken cancellationToken = default) {
        var normalized = channelType is null ? null : NormalizeChannelType(channelType);
        return _toolingClient.GetSelectableEntitiesAsync(normalized, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the local mirror from Salesforce so channels created, changed or removed in Setup are
    /// reflected here.
    /// </summary>
    /// <remarks>
    /// Each channel and member is retrieved individually because list queries return IDs rather than the
    /// readable channel and entity names. Orgs are capped at 100 channels, so the call volume is bounded
    /// and this is an explicit admin action rather than a hot path.
    /// </remarks>
    public async Task<List<PlatformEventChannelEntity>> ResyncFromSalesforceAsync(CancellationToken cancellationToken = default) {
        var summaries = await _toolingClient.ListChannelsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var seenSfIds = new List<string>();

        foreach (var summary in summaries) {
            if (summary.Id is null) {
                continue;
            }

            var channel = await _toolingClient.GetChannelAsync(summary.Id, cancellationToken).ConfigureAwait(false);
            if (channel is null) {
                continue;
            }
            channel.Id ??= summary.Id;

            var mirrored = await MirrorChannelAsync(channel, cancellationToken).ConfigureAwait(false);
            seenSfIds.Add(mirrored.SfId);

            var memberSummaries = await _toolingClient
                .ListChannelMembersAsync(summary.Id, cancellationToken).ConfigureAwait(false);

            var members = new List<PlatformEventChannelMemberEntity>();
            foreach (var memberSummary in memberSummaries) {
                if (memberSummary.Id is null) {
                    continue;
                }
                var member = await _toolingClient
                    .GetChannelMemberAsync(memberSummary.Id, cancellationToken).ConfigureAwait(false);
                if (member is null) {
                    continue;
                }
                member.Id ??= memberSummary.Id;
                members.Add(MapMember(member, mirrored.Id, member.FullName));
            }

            await _repo.ReplaceMembersForChannelAsync(mirrored.Id, members, cancellationToken).ConfigureAwait(false);
        }

        var removed = await _repo.DeleteChannelsNotInAsync(seenSfIds, cancellationToken).ConfigureAwait(false);
        if (removed > 0) {
            _logger.LogInformation("Resync removed {Count} channel(s) no longer present in Salesforce", removed);
        }

        return await _repo.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Helpers

    private async Task<PlatformEventChannelEntity> RequireChannelAsync(int id, CancellationToken cancellationToken) {
        return await _repo.GetChannelByIdAsync(id, cancellationToken).ConfigureAwait(false)
               ?? throw new KeyNotFoundException($"No platform event channel with ID {id}.");
    }

    private async Task<PlatformEventChannelMemberEntity> RequireMemberAsync(int id, CancellationToken cancellationToken) {
        return await _repo.GetMemberByIdAsync(id, cancellationToken).ConfigureAwait(false)
               ?? throw new KeyNotFoundException($"No platform event channel member with ID {id}.");
    }

    /// <summary>
    /// Writes a Salesforce channel into the local mirror. A mirror failure after a successful Salesforce
    /// write is logged and rethrown — resync repairs the row; the Salesforce change is never rolled back.
    /// </summary>
    private async Task<PlatformEventChannelEntity> MirrorChannelAsync(PlatformEventChannel channel, CancellationToken cancellationToken) {
        try {
            return await _repo.UpsertChannelAsync(MapChannel(channel), cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(ex,
                "Channel {SfId} was saved in Salesforce but the local mirror could not be updated. Run a resync to repair it.",
                channel.Id);
            throw;
        }
    }

    private static PlatformEventChannelEntity MapChannel(PlatformEventChannel channel) {
        var developerName = channel.DeveloperName
                            ?? throw new InvalidOperationException("Salesforce returned a channel without a DeveloperName.");

        return new PlatformEventChannelEntity {
            SfId = channel.Id ?? throw new InvalidOperationException("Salesforce returned a channel without an Id."),
            FullName = channel.FullName ?? BuildChannelFullName(developerName, channel.NamespacePrefix),
            DeveloperName = developerName,
            MasterLabel = channel.MasterLabel ?? channel.Metadata?.Label,
            ChannelType = channel.ChannelType ?? channel.Metadata?.ChannelType
                          ?? throw new InvalidOperationException("Salesforce returned a channel without a ChannelType."),
            EventType = channel.EventType ?? channel.Metadata?.EventType,
            NamespacePrefix = channel.NamespacePrefix,
            ManageableState = channel.ManageableState
        };
    }

    private static PlatformEventChannelMemberEntity MapMember(PlatformEventChannelMember member, int channelId, string? fullName) {
        // Metadata carries the readable entity name; the top-level field is an EntityDefinition ID.
        var selectedEntity = member.Metadata?.SelectedEntity ?? member.SelectedEntity
            ?? throw new InvalidOperationException("Salesforce returned a channel member without a SelectedEntity.");

        var enrichedFieldNames = member.Metadata?.EnrichedFields?
            .Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        return new PlatformEventChannelMemberEntity {
            ChannelId = channelId,
            SfId = member.Id ?? throw new InvalidOperationException("Salesforce returned a channel member without an Id."),
            FullName = member.FullName ?? fullName ?? member.DeveloperName
                       ?? throw new InvalidOperationException("Salesforce returned a channel member without a FullName."),
            DeveloperName = member.DeveloperName,
            SelectedEntity = selectedEntity,
            FilterExpression = member.Metadata?.FilterExpression ?? member.FilterExpression,
            EnrichedFields = enrichedFieldNames is { Count: > 0 } ? JsonConvert.SerializeObject(enrichedFieldNames) : null
        };
    }

    private static string BuildChannelFullName(string developerName, string? namespacePrefix) =>
        string.IsNullOrWhiteSpace(namespacePrefix)
            ? $"{developerName}{ChannelSuffix}"
            : $"{namespacePrefix}__{developerName}{ChannelSuffix}";

    private static List<EnrichedField>? ToEnrichedFields(List<string>? names) {
        if (names is null || names.Count == 0) {
            return null;
        }
        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => new EnrichedField { Name = n.Trim() })
            .ToList();
    }

    private static void ValidateChannelFullName(string fullName) {
        if (string.IsNullOrWhiteSpace(fullName)) {
            throw new ValidationException("FullName is required.");
        }
        if (!fullName.EndsWith(ChannelSuffix, StringComparison.Ordinal)) {
            throw new ValidationException($"FullName must end with '{ChannelSuffix}', for example 'SalesEvents{ChannelSuffix}'.");
        }

        var developerName = fullName[..^ChannelSuffix.Length];
        if (!DeveloperNamePattern.IsMatch(developerName)) {
            throw new ValidationException(
                $"'{developerName}' is not a valid channel name. It must start with a letter, contain only letters, " +
                "numbers and single underscores, and must not end with an underscore.");
        }
    }

    private static string NormalizeChannelType(string? channelType) {
        var value = channelType?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || !ValidChannelTypes.Contains(value)) {
            throw new ValidationException(
                $"ChannelType must be one of: {string.Join(", ", ValidChannelTypes)}.");
        }
        return value;
    }

    private static string? NormalizeEventType(string? eventType) {
        if (string.IsNullOrWhiteSpace(eventType)) {
            return null;
        }
        var value = eventType.Trim().ToLowerInvariant();
        if (!ValidEventTypes.Contains(value)) {
            throw new ValidationException(
                $"EventType must be one of: {string.Join(", ", ValidEventTypes)}.");
        }
        return value;
    }

    /// <summary>
    /// A channel carries exactly one event product, so Change Data Capture entities and platform events
    /// cannot be mixed.
    /// </summary>
    private static void ValidateEntityMatchesChannelType(string selectedEntity, string channelType) {
        var isChangeEvent = selectedEntity.EndsWith(ChangeEventSuffix, StringComparison.Ordinal);

        if (string.Equals(channelType, ChannelTypeData, StringComparison.OrdinalIgnoreCase) && !isChangeEvent) {
            throw new ValidationException(
                $"'{selectedEntity}' is not a Change Data Capture entity, so it cannot be added to a '{ChannelTypeData}' channel.");
        }
        if (string.Equals(channelType, ChannelTypeEvent, StringComparison.OrdinalIgnoreCase) && isChangeEvent) {
            throw new ValidationException(
                $"'{selectedEntity}' is a Change Data Capture entity, so it cannot be added to an '{ChannelTypeEvent}' channel.");
        }
    }

    #endregion
}
