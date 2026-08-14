using Database.Models;
using DTO;
using Salesforce.Dtos;

namespace Application.Services.Interfaces;

/// <summary>
/// Creates and manages Salesforce platform event channels and their members.
/// </summary>
/// <remarks>
/// Salesforce is the source of truth. Writes go to the Tooling API first and the local mirror is only
/// updated once Salesforce has accepted the change; reads are served from the mirror, which
/// <see cref="ResyncFromSalesforceAsync"/> rebuilds.
/// </remarks>
public interface IPlatformEventService {
    Task<List<PlatformEventChannelEntity>> GetChannelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a channel with its members, or null when no such channel is mirrored.</summary>
    Task<PlatformEventChannelEntity?> GetChannelAsync(int id, CancellationToken cancellationToken = default);

    Task<PlatformEventChannelEntity> CreateChannelAsync(CreateChannelDTO request, CancellationToken cancellationToken = default);
    Task<PlatformEventChannelEntity> UpdateChannelAsync(int id, UpdateChannelDTO request, CancellationToken cancellationToken = default);
    Task DeleteChannelAsync(int id, CancellationToken cancellationToken = default);

    Task<List<PlatformEventChannelMemberEntity>> GetChannelMembersAsync(int channelId, CancellationToken cancellationToken = default);
    Task<PlatformEventChannelMemberEntity> AddChannelMemberAsync(int channelId, CreateChannelMemberDTO request, CancellationToken cancellationToken = default);
    Task<PlatformEventChannelMemberEntity> UpdateChannelMemberAsync(int memberId, UpdateChannelMemberDTO request, CancellationToken cancellationToken = default);
    Task RemoveChannelMemberAsync(int memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the entities that may be added to a channel of the given type.
    /// </summary>
    Task<List<ToolingPicklistValue>> GetSelectableEntitiesAsync(string? channelType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the local mirror from Salesforce, picking up channels created or removed in Setup.
    /// Returns the resulting channels.
    /// </summary>
    Task<List<PlatformEventChannelEntity>> ResyncFromSalesforceAsync(CancellationToken cancellationToken = default);
}
