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
    /// <summary>
    /// Creates (or adopts) a Change Data Capture Channel with its Starting Point, optionally making it the Primary
    /// Channel, and says which happened.
    /// </summary>
    Task<NewChannelResultDTO> CreateDataChannelAsync(NewChannelDTO request, CancellationToken cancellationToken = default);

    Task<PlatformEventChannelEntity> UpdateChannelAsync(int id, UpdateChannelDTO request, CancellationToken cancellationToken = default);
    Task DeleteChannelAsync(int id, CancellationToken cancellationToken = default);

    Task<List<PlatformEventChannelMemberEntity>> GetChannelMembersAsync(int channelId, CancellationToken cancellationToken = default);
    Task<PlatformEventChannelMemberEntity> AddChannelMemberAsync(int channelId, CreateChannelMemberDTO request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Adds several Entities to a channel, all or none. A rejection is reported per Entity in the result rather
    /// than thrown; requests invalid before reaching Salesforce still throw a validation error.
    /// </summary>
    Task<AddChannelMembersResultDTO> AddChannelMembersAsync(int channelId, AddChannelMembersDTO request, CancellationToken cancellationToken = default);

    Task<PlatformEventChannelMemberEntity> UpdateChannelMemberAsync(int memberId, UpdateChannelMemberDTO request, CancellationToken cancellationToken = default);
    Task RemoveChannelMemberAsync(int memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the entities that may be added to a channel of the given type.
    /// </summary>
    Task<List<ToolingPicklistValue>> GetSelectableEntitiesAsync(string? channelType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the local mirror from Salesforce, picking up channels created or removed in Setup, and reports
    /// what changed. An Entity found removed from the Primary Channel has its Binding set Inactive, as if it had
    /// been removed here.
    /// </summary>
    Task<ResyncReportDTO> ResyncFromSalesforceAsync(CancellationToken cancellationToken = default);
}
