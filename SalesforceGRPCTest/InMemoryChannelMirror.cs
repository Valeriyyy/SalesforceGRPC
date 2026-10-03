using Database.Models;
using Database.Repositories.Interfaces;

namespace SalesforceGRPCTest;

/// <summary>
/// The Mirror held in memory, behaving as the Postgres repository does where tests can tell: upserts keyed on the
/// Salesforce id keep the local id and the Binding link, a channel read by id carries its members, and deleting a
/// channel takes its members with it.
/// </summary>
internal sealed class InMemoryChannelMirror : IPlatformEventChannelRepository {
    private readonly List<PlatformEventChannelEntity> _channels = [];
    private readonly List<PlatformEventChannelMemberEntity> _members = [];
    private int _nextChannelId = 1000;
    private int _nextMemberId = 1000;

    public IReadOnlyList<PlatformEventChannelEntity> Channels => _channels;
    public IReadOnlyList<PlatformEventChannelMemberEntity> Members => _members;

    public PlatformEventChannelEntity Add(PlatformEventChannelEntity channel) {
        _channels.Add(channel);
        return channel;
    }

    public PlatformEventChannelMemberEntity Add(PlatformEventChannelMemberEntity member) {
        _members.Add(member);
        return member;
    }

    public Task<List<PlatformEventChannelEntity>> GetChannelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_channels.OrderBy(c => c.DeveloperName).ToList());

    public Task<PlatformEventChannelEntity?> GetChannelByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(WithMembers(_channels.FirstOrDefault(c => c.Id == id)));

    public Task<PlatformEventChannelEntity?> GetChannelBySfIdAsync(string sfId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_channels.FirstOrDefault(c => c.SfId == sfId));

    public Task<PlatformEventChannelEntity> UpsertChannelAsync(PlatformEventChannelEntity channel,
        CancellationToken cancellationToken = default) {
        var existing = _channels.FirstOrDefault(c => c.SfId == channel.SfId);
        if (existing is null) {
            channel.Id = _nextChannelId++;
            _channels.Add(channel);
            return Task.FromResult(channel);
        }

        existing.FullName = channel.FullName;
        existing.DeveloperName = channel.DeveloperName;
        existing.MasterLabel = channel.MasterLabel;
        existing.ChannelType = channel.ChannelType;
        existing.EventType = channel.EventType;
        existing.LastSyncedAt = channel.LastSyncedAt;
        return Task.FromResult(existing);
    }

    public Task<bool> DeleteChannelAsync(int id, CancellationToken cancellationToken = default) {
        _members.RemoveAll(m => m.ChannelId == id);
        return Task.FromResult(_channels.RemoveAll(c => c.Id == id) > 0);
    }

    public Task<bool> DeleteChannelBySfIdAsync(string sfId, CancellationToken cancellationToken = default) {
        var channel = _channels.FirstOrDefault(c => c.SfId == sfId);
        return channel is null ? Task.FromResult(false) : DeleteChannelAsync(channel.Id, cancellationToken);
    }

    public Task<List<PlatformEventChannelMemberEntity>> GetMembersByChannelIdAsync(int channelId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.Where(m => m.ChannelId == channelId).ToList());

    public Task<PlatformEventChannelMemberEntity?> GetMemberByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.FirstOrDefault(m => m.Id == id));

    public Task<PlatformEventChannelMemberEntity?> GetMemberBySfIdAsync(string sfId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.FirstOrDefault(m => m.SfId == sfId));

    public Task<PlatformEventChannelMemberEntity> UpsertMemberAsync(PlatformEventChannelMemberEntity member,
        CancellationToken cancellationToken = default) {
        var existing = _members.FirstOrDefault(m => m.SfId == member.SfId);
        if (existing is null) {
            member.Id = _nextMemberId++;
            _members.Add(member);
            return Task.FromResult(member);
        }

        existing.FilterExpression = member.FilterExpression;
        existing.EnrichedFields = member.EnrichedFields;
        existing.LastSyncedAt = member.LastSyncedAt;
        return Task.FromResult(existing);
    }

    public Task<bool> DeleteMemberBySfIdAsync(string sfId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.RemoveAll(m => m.SfId == sfId) > 0);

    public async Task ReplaceMembersForChannelAsync(int channelId, IEnumerable<PlatformEventChannelMemberEntity> members,
        CancellationToken cancellationToken = default) {
        var incoming = members.ToList();
        var keep = incoming.Select(m => m.SfId).ToHashSet();
        _members.RemoveAll(m => m.ChannelId == channelId && !keep.Contains(m.SfId));
        foreach (var member in incoming) {
            member.ChannelId = channelId;
            await UpsertMemberAsync(member, cancellationToken);
        }
    }

    public Task<int> DeleteChannelsNotInAsync(IEnumerable<string> sfIdsToKeep, CancellationToken cancellationToken = default) {
        var keep = sfIdsToKeep.ToHashSet();
        var gone = _channels.Where(c => !keep.Contains(c.SfId)).Select(c => c.Id).ToList();
        foreach (var id in gone) {
            DeleteChannelAsync(id, cancellationToken);
        }
        return Task.FromResult(gone.Count);
    }

    public Task<PlatformEventChannelEntity?> GetPrimaryChannelAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(WithMembers(_channels.FirstOrDefault(c => c.IsPrimary)));

    public Task<bool> SetPrimaryChannelAsync(int channelId, CancellationToken cancellationToken = default) {
        foreach (var channel in _channels) {
            channel.IsPrimary = channel.Id == channelId;
        }
        return Task.FromResult(_channels.Any(c => c.Id == channelId));
    }

    public Task<bool> SetStartingPointAsync(int channelId, StartingPoint startingPoint,
        CancellationToken cancellationToken = default) {
        var channel = _channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is not null) {
            channel.StartingPoint = startingPoint;
        }
        return Task.FromResult(channel is not null);
    }

    public Task ClearPrimaryChannelAsync(CancellationToken cancellationToken = default) {
        foreach (var channel in _channels) {
            channel.IsPrimary = false;
        }
        return Task.CompletedTask;
    }

    public Task<bool> SetMemberBindingAsync(int memberId, int? cdcSchemaId, CancellationToken cancellationToken = default) {
        var member = _members.FirstOrDefault(m => m.Id == memberId);
        if (member is not null) {
            member.CdcSchemaId = cdcSchemaId;
        }
        return Task.FromResult(member is not null);
    }

    public Task<List<PlatformEventChannelMemberEntity>> GetMembersByBindingIdAsync(int cdcSchemaId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.Where(m => m.CdcSchemaId == cdcSchemaId).ToList());

    private PlatformEventChannelEntity? WithMembers(PlatformEventChannelEntity? channel) {
        if (channel is not null) {
            channel.Members = _members.Where(m => m.ChannelId == channel.Id).ToList();
        }
        return channel;
    }
}
