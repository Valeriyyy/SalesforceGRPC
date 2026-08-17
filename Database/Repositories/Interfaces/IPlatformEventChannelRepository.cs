using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// Reads and writes the local mirror of Salesforce platform event channels and their members.
/// Every write here follows a successful Tooling API call — this store never originates channel state.
/// </summary>
public interface IPlatformEventChannelRepository {
    Task<List<PlatformEventChannelEntity>> GetChannelsAsync(CancellationToken cancellationToken = default);
    Task<PlatformEventChannelEntity?> GetChannelByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PlatformEventChannelEntity?> GetChannelBySfIdAsync(string sfId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates a channel, keyed on its Salesforce ID.</summary>
    Task<PlatformEventChannelEntity> UpsertChannelAsync(PlatformEventChannelEntity channel, CancellationToken cancellationToken = default);

    Task<bool> DeleteChannelAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteChannelBySfIdAsync(string sfId, CancellationToken cancellationToken = default);

    Task<List<PlatformEventChannelMemberEntity>> GetMembersByChannelIdAsync(int channelId, CancellationToken cancellationToken = default);
    Task<PlatformEventChannelMemberEntity?> GetMemberByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PlatformEventChannelMemberEntity?> GetMemberBySfIdAsync(string sfId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates a channel member, keyed on its Salesforce ID.</summary>
    Task<PlatformEventChannelMemberEntity> UpsertMemberAsync(PlatformEventChannelMemberEntity member, CancellationToken cancellationToken = default);

    Task<bool> DeleteMemberBySfIdAsync(string sfId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a channel's members with the supplied set in one transaction, deleting any local rows
    /// no longer present in Salesforce. Used by resync.
    /// </summary>
    Task ReplaceMembersForChannelAsync(int channelId, IEnumerable<PlatformEventChannelMemberEntity> members, CancellationToken cancellationToken = default);

    /// <summary>Deletes local channels whose Salesforce IDs are not in the supplied set. Used by resync.</summary>
    Task<int> DeleteChannelsNotInAsync(IEnumerable<string> sfIdsToKeep, CancellationToken cancellationToken = default);

    /// <summary>The Primary Channel with its members, or null when none has been selected.</summary>
    Task<PlatformEventChannelEntity?> GetPrimaryChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>Makes one channel the Primary Channel, clearing the flag from every other row.</summary>
    Task<bool> SetPrimaryChannelAsync(int channelId, CancellationToken cancellationToken = default);

    /// <summary>Clears the Primary Channel flag from every channel, leaving the worker with nothing to do.</summary>
    Task ClearPrimaryChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>Points a Channel Member at its Binding, or clears the link when the id is null.</summary>
    Task<bool> SetMemberBindingAsync(int memberId, int? cdcSchemaId, CancellationToken cancellationToken = default);

    /// <summary>Every Channel Member pointing at a Binding, across all channels.</summary>
    Task<List<PlatformEventChannelMemberEntity>> GetMembersByBindingIdAsync(int cdcSchemaId, CancellationToken cancellationToken = default);
}
