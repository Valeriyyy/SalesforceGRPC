using Application.Services;
using Database.Models;
using DTO;
using Newtonsoft.Json;
using Salesforce.Dtos;

namespace Application.Mappers;

/// <summary>
/// Turns Salesforce Tooling API channels and members into Mirror rows, and Mirror rows into DTOs.
/// </summary>
public static class ChannelMapper {
    public static PlatformEventChannelEntity ToEntity(this PlatformEventChannel channel) {
        var developerName = channel.DeveloperName
                            ?? throw new InvalidOperationException("Salesforce returned a channel without a DeveloperName.");

        return new PlatformEventChannelEntity {
            SfId = channel.Id ?? throw new InvalidOperationException("Salesforce returned a channel without an Id."),
            FullName = channel.FullName ?? ChannelFullName.For(developerName, channel.NamespacePrefix),
            DeveloperName = developerName,
            MasterLabel = channel.MasterLabel ?? channel.Metadata?.Label,
            ChannelType = channel.ChannelType ?? channel.Metadata?.ChannelType
                          ?? throw new InvalidOperationException("Salesforce returned a channel without a ChannelType."),
            EventType = channel.EventType ?? channel.Metadata?.EventType,
            NamespacePrefix = channel.NamespacePrefix,
            ManageableState = channel.ManageableState
        };
    }

    public static PlatformEventChannelMemberEntity ToEntity(this PlatformEventChannelMember member, int channelId,
        string? fullName) {
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

    /// <summary>The Tooling API shape of a list of Enriched Field names, or null when there are none.</summary>
    public static List<EnrichedField>? ToEnrichedFields(List<string>? names) {
        if (names is null || names.Count == 0) {
            return null;
        }
        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => new EnrichedField { Name = n.Trim() })
            .ToList();
    }

    public static ChannelStartDTO ToStartDto(this PlatformEventChannelEntity channel, Checkpoint? checkpoint,
        bool canResume) => new() {
        ChannelId = channel.Id,
        StartingPoint = channel.StartingPoint.ToString(),
        HasCheckpoint = checkpoint is not null,
        CheckpointSavedAt = checkpoint?.SavedAt,
        CanResume = canResume
    };
}
