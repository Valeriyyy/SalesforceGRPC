using Application.Bindings;
using Application.Mappers;
using Application.Services.Interfaces;
using Database.Models;
using Database.Repositories.Interfaces;
using DTO;
using Microsoft.Extensions.Logging;
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
    private readonly IMetaRepository _meta;
    private readonly IConfigurationChangeSignal _changeSignal;
    private readonly ILogger<PlatformEventService> _logger;

    public PlatformEventService(SalesforceToolingClient toolingClient, IPlatformEventChannelRepository repo,
        IMetaRepository meta, IConfigurationChangeSignal changeSignal, ILogger<PlatformEventService> logger) {
        _toolingClient = toolingClient;
        _repo = repo;
        _meta = meta;
        _changeSignal = changeSignal;
        _logger = logger;
    }

    #region Channels

    public Task<List<PlatformEventChannelEntity>> GetChannelsAsync(CancellationToken cancellationToken = default) =>
        _repo.GetChannelsAsync(cancellationToken);

    public Task<PlatformEventChannelEntity?> GetChannelAsync(int id, CancellationToken cancellationToken = default) =>
        _repo.GetChannelByIdAsync(id, cancellationToken);

    /// <summary>
    /// Creates a channel in Salesforce and mirrors it locally. If a channel with the same developer name
    /// already exists in Salesforce — e.g. left behind after the local mirror was deleted and the user is
    /// re-creating the org connection — it is adopted, along with any members it already has, instead of
    /// failing on a duplicate-value error from Salesforce.
    /// </summary>
    public async Task<PlatformEventChannelEntity> CreateChannelAsync(CreateChannelDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        var (channel, _) = await CreateOrAdoptAsync(request, cancellationToken).ConfigureAwait(false);
        return channel;
    }

    /// <summary>
    /// Creates (or adopts) a Change Data Capture Channel, sets where it starts, and optionally makes it the Primary
    /// Channel. A Channel new to the Mirror has no Checkpoint — one never outlives its Mirror row — so there is no
    /// Resume choice to make and it can become the Primary Channel directly.
    /// </summary>
    public async Task<NewChannelResultDTO> CreateDataChannelAsync(NewChannelDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var startingPoint = StartingPoint.Latest;
        if (!string.IsNullOrWhiteSpace(request.StartingPoint) &&
            (!Enum.TryParse(request.StartingPoint, ignoreCase: true, out startingPoint) || !Enum.IsDefined(startingPoint))) {
            throw new ValidationException(
                $"'{request.StartingPoint}' is not a Starting Point. Choose {nameof(StartingPoint.Latest)} or {nameof(StartingPoint.Earliest)}.");
        }

        // A Channel already here may have a Checkpoint, and making it Primary must then offer Resume, Earliest or
        // Latest — which only its own page does. Creating it again would adopt it and skip that choice.
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var mirrored = await _repo.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
        if (mirrored.FirstOrDefault(c => string.Equals(c.FullName, fullName, StringComparison.OrdinalIgnoreCase)) is { } existing) {
            throw new ValidationException($"{existing.FullName} already exists here. Open it from the Channels list instead.");
        }

        var (channel, adopted) = await CreateOrAdoptAsync(new CreateChannelDTO {
            FullName = request.FullName, Label = request.Label, ChannelType = ChannelTypeData
        }, cancellationToken).ConfigureAwait(false);

        if (!channel.IsChangeDataCapture) {
            throw new ValidationException(
                $"Salesforce already has a channel named '{channel.FullName}', but it carries platform events, not Change Data Capture. Choose another name.");
        }

        await _repo.SetStartingPointAsync(channel.Id, startingPoint, cancellationToken).ConfigureAwait(false);
        if (request.MakePrimary) {
            await _repo.SetPrimaryChannelAsync(channel.Id, cancellationToken).ConfigureAwait(false);
            _changeSignal.Signal();
            _logger.LogInformation("Primary Channel set to new Channel {FullName}, starting from {StartingPoint}",
                channel.FullName, startingPoint);
        }

        var members = await _repo.GetMembersByChannelIdAsync(channel.Id, cancellationToken).ConfigureAwait(false);
        return new NewChannelResultDTO {
            ChannelId = channel.Id, FullName = channel.FullName, Adopted = adopted, MemberCount = members.Count
        };
    }

    /// <summary>
    /// Creates a channel in Salesforce and mirrors it locally. If a channel with the same developer name
    /// already exists in Salesforce — e.g. left behind after the local mirror was deleted and the user is
    /// re-creating the org connection — it is adopted, along with any members it already has, instead of
    /// failing on a duplicate-value error from Salesforce.
    /// </summary>
    private async Task<(PlatformEventChannelEntity Channel, bool Adopted)> CreateOrAdoptAsync(CreateChannelDTO request,
        CancellationToken cancellationToken) {
        var fullName = request.FullName?.Trim() ?? string.Empty;
        ValidateChannelFullName(fullName);

        if (string.IsNullOrWhiteSpace(request.Label)) {
            throw new ValidationException("Label is required.");
        }

        var channelType = NormalizeChannelType(request.ChannelType);
        var eventType = NormalizeEventType(request.EventType);

        var developerName = fullName[..^ChannelFullName.Suffix.Length];
        var existing = await _toolingClient.GetChannelByDeveloperNameAsync(developerName, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null) {
            _logger.LogInformation(
                "Channel {FullName} already exists in Salesforce (Id {SfId}); adopting it and its members instead of creating a new one.",
                fullName, existing.Id);
            var adopted = await MirrorChannelAsync(existing, cancellationToken).ConfigureAwait(false);
            await SyncMembersForChannelAsync(adopted.SfId, adopted.Id, cancellationToken).ConfigureAwait(false);
            return (adopted, true);
        }

        var saveResult = await _toolingClient
            .CreateChannelAsync(fullName, request.Label.Trim(), channelType, eventType, cancellationToken)
            .ConfigureAwait(false);

        var created = await _toolingClient.GetChannelAsync(saveResult.Id!, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(
                          $"Channel {saveResult.Id} was created but could not be read back from Salesforce.");

        return (await MirrorChannelAsync(created, cancellationToken).ConfigureAwait(false), false);
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
    /// failure names the member that blocked it, though Salesforce would also cascade them. The Channel's
    /// Checkpoint goes with its Mirror row.
    /// </summary>
    public async Task DeleteChannelAsync(int id, CancellationToken cancellationToken = default) {
        var existing = await RequireChannelAsync(id, cancellationToken).ConfigureAwait(false);

        foreach (var member in existing.Members) {
            await _toolingClient.DeleteChannelMemberAsync(member.SfId, cancellationToken).ConfigureAwait(false);
        }

        await _toolingClient.DeleteChannelAsync(existing.SfId, cancellationToken).ConfigureAwait(false);
        await _repo.DeleteChannelAsync(existing.Id, cancellationToken).ConfigureAwait(false);

        // Deleting the Primary Channel is clearing it: nothing streams, and no Binding changes state — its
        // members leave as a side effect of the Channel going, not as a decision about each Entity.
        if (existing.IsPrimary) {
            _logger.LogWarning("Deleted the Primary Channel {FullName}; nothing streams until another is chosen",
                existing.FullName);
            _changeSignal.Signal();
        }
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
            EnrichedFields = ChannelMapper.ToEnrichedFields(request.EnrichedFields)
        };

        var fullName = SalesforceToolingClient.BuildMemberFullName(channel.FullName, selectedEntity);

        var saveResult = await _toolingClient
            .CreateChannelMemberAsync(fullName, metadata, cancellationToken).ConfigureAwait(false);

        var created = await _toolingClient.GetChannelMemberAsync(saveResult.Id!, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(
                          $"Channel member {saveResult.Id} was created but could not be read back from Salesforce.");

        var mirrored = await MirrorMemberAsync(created.ToEntity(channel.Id, fullName), cancellationToken).ConfigureAwait(false);
        if (channel.IsPrimary) {
            _changeSignal.Signal();
        }
        return mirrored;
    }

    public async Task<AddChannelMembersResultDTO> AddChannelMembersAsync(int channelId, AddChannelMembersDTO request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var channel = await RequireChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        var requested = request.Members ?? [];
        if (requested.Count == 0) {
            throw new ValidationException("Choose at least one Entity to add.");
        }

        // Everything is checked before Salesforce is called, so a submission it was always going to refuse
        // costs no callout and leaves nothing to roll back.
        var carried = channel.Members.Select(m => m.SelectedEntity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var members = new List<(string FullName, PlatformEventChannelMemberMetadata Metadata)>();
        foreach (var item in requested) {
            var entity = item.SelectedEntity?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entity)) {
                throw new ValidationException("Every Entity to add needs a name.");
            }
            ValidateEntityMatchesChannelType(entity, channel.ChannelType);
            if (carried.Contains(entity)) {
                throw new ValidationException($"'{channel.FullName}' already carries {entity}.");
            }
            if (!chosen.Add(entity)) {
                throw new ValidationException($"{entity} was chosen more than once.");
            }

            members.Add((SalesforceToolingClient.BuildMemberFullName(channel.FullName, entity),
                new PlatformEventChannelMemberMetadata {
                    EventChannel = channel.FullName,
                    SelectedEntity = entity,
                    FilterExpression = string.IsNullOrWhiteSpace(item.FilterExpression) ? null : item.FilterExpression.Trim(),
                    EnrichedFields = ChannelMapper.ToEnrichedFields(item.EnrichedFields)
                }));
        }

        var responses = await _toolingClient.CreateChannelMembersAsync(members, cancellationToken).ConfigureAwait(false);
        if (responses.Count != members.Count) {
            throw new InvalidOperationException(
                $"Salesforce answered {responses.Count} of {members.Count} channel member creates.");
        }

        if (responses.All(r => r.IsSuccess)) {
            var outcomes = new List<ChannelMemberOutcomeDTO>();
            for (var i = 0; i < members.Count; i++) {
                var sfId = responses[i].CreatedId
                           ?? throw new InvalidOperationException($"Salesforce created {members[i].Metadata.SelectedEntity} without returning its id.");
                var created = await _toolingClient.GetChannelMemberAsync(sfId, cancellationToken).ConfigureAwait(false)
                              ?? throw new InvalidOperationException(
                                  $"Channel member {sfId} was created but could not be read back from Salesforce.");
                var mirrored = await MirrorMemberAsync(created.ToEntity(channel.Id, members[i].FullName), cancellationToken)
                    .ConfigureAwait(false);
                outcomes.Add(new ChannelMemberOutcomeDTO {
                    SelectedEntity = mirrored.SelectedEntity, Status = MemberOutcome.Added, MemberId = mirrored.Id
                });
            }

            if (channel.IsPrimary) {
                _changeSignal.Signal();
            }
            return new AddChannelMembersResultDTO { Added = true, Outcomes = outcomes };
        }

        return await RejectChannelMembersAsync(channel, members, responses, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports a refused submission per Entity, first removing any member Salesforce created regardless, so the
    /// submission stays all or nothing even if the composite rollback did not cover these metadata-backed rows.
    /// </summary>
    private async Task<AddChannelMembersResultDTO> RejectChannelMembersAsync(PlatformEventChannelEntity channel,
        List<(string FullName, PlatformEventChannelMemberMetadata Metadata)> members,
        List<ToolingCompositeSubresponse> responses, CancellationToken cancellationToken) {
        var result = new AddChannelMembersResultDTO { Added = false };

        for (var i = 0; i < members.Count; i++) {
            var entity = members[i].Metadata.SelectedEntity!;
            var response = responses[i];

            if (response.CreatedId is { } sfId) {
                try {
                    await _toolingClient.DeleteChannelMemberAsync(sfId, cancellationToken).ConfigureAwait(false);
                } catch (SalesforceToolingException ex) {
                    _logger.LogError(ex, "Could not remove {Entity} ({SfId}) from {Channel} after its submission failed",
                        entity, sfId, channel.FullName);
                    result.LeftInSalesforce.Add(entity);
                }
                result.Outcomes.Add(new ChannelMemberOutcomeDTO { SelectedEntity = entity, Status = MemberOutcome.NotAdded });
                continue;
            }

            // PROCESSING_HALTED marks a subrequest Salesforce skipped because another one failed.
            var errors = response.Errors;
            var halted = errors.Count > 0 && errors.All(e => e.ErrorCode == "PROCESSING_HALTED");
            result.Outcomes.Add(halted
                ? new ChannelMemberOutcomeDTO { SelectedEntity = entity, Status = MemberOutcome.NotAdded }
                : new ChannelMemberOutcomeDTO {
                    SelectedEntity = entity,
                    Status = MemberOutcome.Failed,
                    Message = errors.Count == 0
                        ? $"Salesforce refused it (HTTP {response.HttpStatusCode})."
                        : string.Join(" ", errors.Select(e => e.ErrorCode is null ? e.Message : $"{e.ErrorCode}: {e.Message}"))
                });
        }

        _logger.LogWarning("Salesforce refused adding {Entities} to {Channel}; none were added",
            string.Join(", ", members.Select(m => m.Metadata.SelectedEntity)), channel.FullName);
        return result;
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
            EnrichedFields = ChannelMapper.ToEnrichedFields(request.EnrichedFields)
        };

        await _toolingClient
            .UpdateChannelMemberAsync(existing.SfId, existing.FullName, metadata, cancellationToken)
            .ConfigureAwait(false);

        var updated = await _toolingClient.GetChannelMemberAsync(existing.SfId, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(
                          $"Channel member {existing.SfId} was updated but could not be read back from Salesforce.");

        return await _repo.UpsertMemberAsync(updated.ToEntity(channel.Id, existing.FullName), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes an event/entity from a channel in Salesforce, then drops the local mirror row.
    /// </summary>
    public async Task RemoveChannelMemberAsync(int memberId, CancellationToken cancellationToken = default) {
        var existing = await RequireMemberAsync(memberId, cancellationToken).ConfigureAwait(false);
        var channel = await RequireChannelAsync(existing.ChannelId, cancellationToken).ConfigureAwait(false);

        await _toolingClient.DeleteChannelMemberAsync(existing.SfId, cancellationToken).ConfigureAwait(false);
        await _repo.DeleteMemberBySfIdAsync(existing.SfId, cancellationToken).ConfigureAwait(false);

        if (channel.IsPrimary) {
            await DeactivateUncarriedBindingAsync(existing.SelectedEntity).ConfigureAwait(false);
            _changeSignal.Signal();
        }
    }

    #endregion

    #region Discovery and sync

    public Task<List<ToolingPicklistValue>> GetSelectableEntitiesAsync(string? channelType = null, CancellationToken cancellationToken = default) {
        var normalized = channelType is null ? null : NormalizeChannelType(channelType);
        return _toolingClient.GetSelectableEntitiesAsync(normalized, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the local mirror from Salesforce so channels created, changed or removed in Setup are
    /// reflected here, and reports the difference.
    /// </summary>
    /// <remarks>
    /// Each channel and member is retrieved individually because list queries return IDs rather than the
    /// readable channel and entity names. Orgs are capped at 100 channels, so the call volume is bounded
    /// and this is an explicit admin action rather than a hot path.
    /// </remarks>
    public async Task<ResyncReportDTO> ResyncFromSalesforceAsync(CancellationToken cancellationToken = default) {
        // Snapshotted by value: the upserts below update these same entities in place in some stores.
        var before = new Dictionary<string, (PlatformEventChannelEntity Channel, string? Label, HashSet<string> Entities)>();
        foreach (var channel in await _repo.GetChannelsAsync(cancellationToken).ConfigureAwait(false)) {
            var members = await _repo.GetMembersByChannelIdAsync(channel.Id, cancellationToken).ConfigureAwait(false);
            before[channel.SfId] = (channel, channel.MasterLabel,
                members.Select(m => m.SelectedEntity).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }

        var report = new ResyncReportDTO();
        var primaryChanged = false;
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

            var members = await SyncMembersForChannelAsync(mirrored.SfId, mirrored.Id, cancellationToken).ConfigureAwait(false);
            if (!mirrored.IsChangeDataCapture) {
                continue;
            }

            if (!before.TryGetValue(mirrored.SfId, out var previous)) {
                report.ChannelsAdded.Add(mirrored.FullName);
                continue;
            }

            if (!string.Equals(previous.Label, mirrored.MasterLabel, StringComparison.Ordinal)) {
                report.ChannelsRelabelled.Add(mirrored.FullName);
            }

            var now = members.Select(m => m.SelectedEntity).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in now.Where(e => !previous.Entities.Contains(e)).Order(StringComparer.Ordinal)) {
                report.MembersAdded.Add(new ResyncMemberChangeDTO { Channel = mirrored.FullName, SelectedEntity = entity });
                primaryChanged |= previous.Channel.IsPrimary;
            }
            foreach (var entity in previous.Entities.Where(e => !now.Contains(e)).Order(StringComparer.Ordinal)) {
                // A removal found by Resync is the same act as one made here, so it has the same consequence.
                var deactivated = previous.Channel.IsPrimary
                    ? await DeactivateUncarriedBindingAsync(entity).ConfigureAwait(false)
                    : null;
                report.MembersRemoved.Add(new ResyncMemberChangeDTO {
                    Channel = mirrored.FullName, SelectedEntity = entity, BindingSetInactive = deactivated?.DbSchemaFullName
                });
                primaryChanged |= previous.Channel.IsPrimary;
            }
        }

        var removed = await _repo.DeleteChannelsNotInAsync(seenSfIds, cancellationToken).ConfigureAwait(false);
        if (removed > 0) {
            _logger.LogInformation("Resync removed {Count} channel(s) no longer present in Salesforce", removed);
        }

        var seen = seenSfIds.ToHashSet();
        foreach (var (channel, _, _) in before.Values.Where(b => !seen.Contains(b.Channel.SfId) && b.Channel.IsChangeDataCapture)
                     .OrderBy(b => b.Channel.FullName, StringComparer.Ordinal)) {
            report.ChannelsRemoved.Add(channel.FullName);
            if (channel.IsPrimary) {
                // Losing the Primary Channel is clearing it: nothing streams, and no Binding changes state.
                report.PrimaryChannelRemoved = channel.FullName;
                primaryChanged = true;
                _logger.LogWarning("Resync found the Primary Channel {FullName} deleted in Salesforce; nothing streams until another is chosen",
                    channel.FullName);
            }
        }

        if (primaryChanged) {
            _changeSignal.Signal();
        }
        return report;
    }

    #endregion

    #region Helpers

    private static class MemberOutcome {
        public const string Added = "Added";
        public const string Failed = "Failed";
        public const string NotAdded = "NotAdded";
    }

    private async Task<PlatformEventChannelEntity> RequireChannelAsync(int id, CancellationToken cancellationToken) {
        return await _repo.GetChannelByIdAsync(id, cancellationToken).ConfigureAwait(false)
               ?? throw new KeyNotFoundException($"No platform event channel with ID {id}.");
    }

    /// <summary>
    /// Writes a member into the Mirror and links it to its Entity's Binding when one exists. Bindings are one per
    /// Entity, so a member for a bound Entity always belongs to that Binding — which is what lets removing and
    /// re-adding an Entity restore its configuration.
    /// </summary>
    private async Task<PlatformEventChannelMemberEntity> MirrorMemberAsync(PlatformEventChannelMemberEntity member,
        CancellationToken cancellationToken) {
        var mirrored = await _repo.UpsertMemberAsync(member, cancellationToken).ConfigureAwait(false);
        await LinkToBindingAsync(mirrored, cancellationToken).ConfigureAwait(false);
        return mirrored;
    }

    private async Task LinkToBindingAsync(PlatformEventChannelMemberEntity member, CancellationToken cancellationToken) {
        var binding = await _meta.GetSchemaByEntityName(member.SelectedEntity).ConfigureAwait(false);
        if (binding is not null && member.CdcSchemaId != binding.Id) {
            await _repo.SetMemberBindingAsync(member.Id, binding.Id, cancellationToken).ConfigureAwait(false);
            member.CdcSchemaId = binding.Id;
        }
    }

    /// <summary>
    /// Sets an Entity's Binding Inactive once the Primary Channel no longer carries it, so re-adding the Entity
    /// brings the Binding back switched off rather than silently streaming again. Only an Active Binding moves:
    /// an Incomplete one was never switched on.
    /// </summary>
    /// <returns>The Binding that was set Inactive, or null when none was.</returns>
    private async Task<CDCSchema?> DeactivateUncarriedBindingAsync(string entityName) {
        var binding = await _meta.GetSchemaByEntityName(entityName).ConfigureAwait(false);
        if (binding is not { BindingState: BindingState.Active }) {
            return null;
        }

        await _meta.SetBindingState(binding.Id, BindingState.Inactive).ConfigureAwait(false);
        _logger.LogInformation("{Entity} left the Primary Channel, so Binding {BindingId} to {Table} was set Inactive",
            entityName, binding.Id, binding.DbSchemaFullName);
        return binding;
    }

    private async Task<PlatformEventChannelMemberEntity> RequireMemberAsync(int id, CancellationToken cancellationToken) {
        return await _repo.GetMemberByIdAsync(id, cancellationToken).ConfigureAwait(false)
               ?? throw new KeyNotFoundException($"No platform event channel member with ID {id}.");
    }

    /// <summary>
    /// Replaces a channel's local member mirror with the full member list read back from Salesforce.
    /// </summary>
    /// <remarks>
    /// Each member is retrieved individually because the list query returns IDs rather than the readable
    /// entity name.
    /// </remarks>
    private async Task<List<PlatformEventChannelMemberEntity>> SyncMembersForChannelAsync(
        string channelSfId, int channelId, CancellationToken cancellationToken) {
        var memberSummaries = await _toolingClient
            .ListChannelMembersAsync(channelSfId, cancellationToken).ConfigureAwait(false);

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
            members.Add(member.ToEntity(channelId, member.FullName));
        }

        await _repo.ReplaceMembersForChannelAsync(channelId, members, cancellationToken).ConfigureAwait(false);

        // Re-read so each member carries its local id and its current Binding link, then link any member whose
        // Entity is bound but which is not yet pointing at that Binding.
        var mirrored = await _repo.GetMembersByChannelIdAsync(channelId, cancellationToken).ConfigureAwait(false);
        foreach (var member in mirrored) {
            await LinkToBindingAsync(member, cancellationToken).ConfigureAwait(false);
        }
        return mirrored;
    }

    /// <summary>
    /// Writes a Salesforce channel into the local mirror. A mirror failure after a successful Salesforce
    /// write is logged and rethrown — resync repairs the row; the Salesforce change is never rolled back.
    /// </summary>
    private async Task<PlatformEventChannelEntity> MirrorChannelAsync(PlatformEventChannel channel, CancellationToken cancellationToken) {
        try {
            return await _repo.UpsertChannelAsync(channel.ToEntity(), cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(ex,
                "Channel {SfId} was saved in Salesforce but the local mirror could not be updated. Run a resync to repair it.",
                channel.Id);
            throw;
        }
    }

    private static void ValidateChannelFullName(string fullName) {
        if (string.IsNullOrWhiteSpace(fullName)) {
            throw new ValidationException("FullName is required.");
        }
        if (!fullName.EndsWith(ChannelFullName.Suffix, StringComparison.Ordinal)) {
            throw new ValidationException($"FullName must end with '{ChannelFullName.Suffix}', for example 'SalesEvents{ChannelFullName.Suffix}'.");
        }

        var developerName = fullName[..^ChannelFullName.Suffix.Length];
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
