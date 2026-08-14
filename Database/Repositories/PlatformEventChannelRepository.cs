using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Repositories;

/// <summary>
/// Dapper-backed store for the local mirror of Salesforce platform event channels. Always talks to the
/// app database (ConnectionStrings:appDatabase), like the other metadata repositories.
/// </summary>
public class PlatformEventChannelRepository : IPlatformEventChannelRepository {
    private readonly ILogger<PlatformEventChannelRepository> _logger;
    private readonly string _connectionString;
    private readonly bool _debugQuery;

    private const string ChannelColumns = @"
                id AS Id,
                sf_id AS SfId,
                full_name AS FullName,
                developer_name AS DeveloperName,
                master_label AS MasterLabel,
                channel_type AS ChannelType,
                event_type AS EventType,
                namespace_prefix AS NamespacePrefix,
                manageable_state AS ManageableState,
                date_created AS DateCreated,
                date_updated AS DateUpdated,
                last_synced_at AS LastSyncedAt";

    private const string MemberColumns = @"
                id AS Id,
                channel_id AS ChannelId,
                sf_id AS SfId,
                full_name AS FullName,
                developer_name AS DeveloperName,
                selected_entity AS SelectedEntity,
                filter_expression AS FilterExpression,
                enriched_fields AS EnrichedFields,
                cdc_schema_id AS CdcSchemaId,
                date_created AS DateCreated,
                date_updated AS DateUpdated,
                last_synced_at AS LastSyncedAt";

    public PlatformEventChannelRepository(ILogger<PlatformEventChannelRepository> logger, IConfiguration configuration) {
        _logger = logger;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
        if (configuration.GetConnectionString("appDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("appDatabase")!;
    }

    #region Channels

    /// <summary>
    /// Retrieves all mirrored channels, without their members.
    /// </summary>
    public async Task<List<PlatformEventChannelEntity>> GetChannelsAsync(CancellationToken cancellationToken = default) {
        var sql = $@"
            SELECT {ChannelColumns}
            FROM salesforce.platform_event_channels
            ORDER BY developer_name";

        LogQuery("SELECT", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        var channels = await connection.QueryAsync<PlatformEventChannelEntity>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return channels.ToList();
    }

    /// <summary>
    /// Retrieves a channel by local ID, with its members populated.
    /// </summary>
    public async Task<PlatformEventChannelEntity?> GetChannelByIdAsync(int id, CancellationToken cancellationToken = default) {
        var sql = $@"
            SELECT {ChannelColumns}
            FROM salesforce.platform_event_channels
            WHERE id = @Id";

        LogQuery("SELECT", sql, new { Id = id });

        await using var connection = new NpgsqlConnection(_connectionString);
        var channel = await connection.QuerySingleOrDefaultAsync<PlatformEventChannelEntity>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (channel is not null) {
            channel.Members = await GetMembersByChannelIdAsync(channel.Id, cancellationToken).ConfigureAwait(false);
        }
        return channel;
    }

    /// <summary>
    /// Retrieves a channel by its Salesforce ID, without its members.
    /// </summary>
    public async Task<PlatformEventChannelEntity?> GetChannelBySfIdAsync(string sfId, CancellationToken cancellationToken = default) {
        var sql = $@"
            SELECT {ChannelColumns}
            FROM salesforce.platform_event_channels
            WHERE sf_id = @SfId";

        LogQuery("SELECT", sql, new { SfId = sfId });

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<PlatformEventChannelEntity>(
            new CommandDefinition(sql, new { SfId = sfId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates a channel keyed on its Salesforce ID, so resync is idempotent.
    /// </summary>
    public async Task<PlatformEventChannelEntity> UpsertChannelAsync(PlatformEventChannelEntity channel, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(channel);

        var sql = $@"
            INSERT INTO salesforce.platform_event_channels
                (sf_id, full_name, developer_name, master_label, channel_type, event_type,
                 namespace_prefix, manageable_state, last_synced_at)
            VALUES
                (@SfId, @FullName, @DeveloperName, @MasterLabel, @ChannelType, @EventType,
                 @NamespacePrefix, @ManageableState, @LastSyncedAt)
            ON CONFLICT (sf_id) DO UPDATE SET
                full_name = EXCLUDED.full_name,
                developer_name = EXCLUDED.developer_name,
                master_label = EXCLUDED.master_label,
                channel_type = EXCLUDED.channel_type,
                event_type = EXCLUDED.event_type,
                namespace_prefix = EXCLUDED.namespace_prefix,
                manageable_state = EXCLUDED.manageable_state,
                date_updated = now(),
                last_synced_at = EXCLUDED.last_synced_at
            RETURNING {ChannelColumns}";

        var parameters = new {
            channel.SfId,
            channel.FullName,
            channel.DeveloperName,
            channel.MasterLabel,
            channel.ChannelType,
            channel.EventType,
            channel.NamespacePrefix,
            channel.ManageableState,
            LastSyncedAt = DateTime.UtcNow
        };

        LogQuery("UPSERT", sql, parameters);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<PlatformEventChannelEntity>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a channel by local ID. Members cascade.
    /// </summary>
    public async Task<bool> DeleteChannelAsync(int id, CancellationToken cancellationToken = default) {
        const string sql = "DELETE FROM salesforce.platform_event_channels WHERE id = @Id";

        LogQuery("DELETE", sql, new { Id = id });

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return affectedRows > 0;
    }

    /// <summary>
    /// Deletes a channel by its Salesforce ID. Members cascade.
    /// </summary>
    public async Task<bool> DeleteChannelBySfIdAsync(string sfId, CancellationToken cancellationToken = default) {
        const string sql = "DELETE FROM salesforce.platform_event_channels WHERE sf_id = @SfId";

        LogQuery("DELETE", sql, new { SfId = sfId });

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { SfId = sfId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return affectedRows > 0;
    }

    /// <summary>
    /// Deletes mirrored channels whose Salesforce IDs are absent from the supplied set — the rows for
    /// channels that were removed in Salesforce since the last sync.
    /// </summary>
    public async Task<int> DeleteChannelsNotInAsync(IEnumerable<string> sfIdsToKeep, CancellationToken cancellationToken = default) {
        var ids = sfIdsToKeep as string[] ?? sfIdsToKeep.ToArray();

        // An empty keep-set means Salesforce has no channels at all, so every local row is stale.
        var sql = ids.Length == 0
            ? "DELETE FROM salesforce.platform_event_channels"
            : "DELETE FROM salesforce.platform_event_channels WHERE sf_id <> ALL(@SfIds)";

        LogQuery("DELETE", sql, new { SfIds = ids });

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteAsync(
            new CommandDefinition(sql, new { SfIds = ids }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    #endregion

    #region Members

    /// <summary>
    /// Retrieves the mirrored members of a channel.
    /// </summary>
    public async Task<List<PlatformEventChannelMemberEntity>> GetMembersByChannelIdAsync(int channelId, CancellationToken cancellationToken = default) {
        var sql = $@"
            SELECT {MemberColumns}
            FROM salesforce.platform_event_channel_members
            WHERE channel_id = @ChannelId
            ORDER BY selected_entity";

        LogQuery("SELECT", sql, new { ChannelId = channelId });

        await using var connection = new NpgsqlConnection(_connectionString);
        var members = await connection.QueryAsync<PlatformEventChannelMemberEntity>(
            new CommandDefinition(sql, new { ChannelId = channelId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return members.ToList();
    }

    /// <summary>
    /// Retrieves a channel member by local ID.
    /// </summary>
    public async Task<PlatformEventChannelMemberEntity?> GetMemberByIdAsync(int id, CancellationToken cancellationToken = default) {
        var sql = $@"
            SELECT {MemberColumns}
            FROM salesforce.platform_event_channel_members
            WHERE id = @Id";

        LogQuery("SELECT", sql, new { Id = id });

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<PlatformEventChannelMemberEntity>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a channel member by its Salesforce ID.
    /// </summary>
    public async Task<PlatformEventChannelMemberEntity?> GetMemberBySfIdAsync(string sfId, CancellationToken cancellationToken = default) {
        var sql = $@"
            SELECT {MemberColumns}
            FROM salesforce.platform_event_channel_members
            WHERE sf_id = @SfId";

        LogQuery("SELECT", sql, new { SfId = sfId });

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<PlatformEventChannelMemberEntity>(
            new CommandDefinition(sql, new { SfId = sfId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates a channel member keyed on its Salesforce ID.
    /// </summary>
    public async Task<PlatformEventChannelMemberEntity> UpsertMemberAsync(PlatformEventChannelMemberEntity member, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(member);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await UpsertMemberAsync(connection, null, member, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rewrites a channel's members to exactly the supplied set, in one transaction, dropping local rows
    /// for members that no longer exist in Salesforce.
    /// </summary>
    public async Task ReplaceMembersForChannelAsync(int channelId, IEnumerable<PlatformEventChannelMemberEntity> members, CancellationToken cancellationToken = default) {
        var incoming = members as PlatformEventChannelMemberEntity[] ?? members.ToArray();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try {
            var keepIds = incoming.Select(m => m.SfId).ToArray();
            var deleteSql = keepIds.Length == 0
                ? "DELETE FROM salesforce.platform_event_channel_members WHERE channel_id = @ChannelId"
                : "DELETE FROM salesforce.platform_event_channel_members WHERE channel_id = @ChannelId AND sf_id <> ALL(@SfIds)";

            LogQuery("DELETE", deleteSql, new { ChannelId = channelId, SfIds = keepIds });

            await connection.ExecuteAsync(new CommandDefinition(deleteSql,
                new { ChannelId = channelId, SfIds = keepIds }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            foreach (var member in incoming) {
                member.ChannelId = channelId;
                await UpsertMemberAsync(connection, transaction, member, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        } catch {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Deletes a channel member by its Salesforce ID.
    /// </summary>
    public async Task<bool> DeleteMemberBySfIdAsync(string sfId, CancellationToken cancellationToken = default) {
        const string sql = "DELETE FROM salesforce.platform_event_channel_members WHERE sf_id = @SfId";

        LogQuery("DELETE", sql, new { SfId = sfId });

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { SfId = sfId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return affectedRows > 0;
    }

    private async Task<PlatformEventChannelMemberEntity> UpsertMemberAsync(NpgsqlConnection connection,
        System.Data.Common.DbTransaction? transaction, PlatformEventChannelMemberEntity member, CancellationToken cancellationToken) {
        var sql = $@"
            INSERT INTO salesforce.platform_event_channel_members
                (channel_id, sf_id, full_name, developer_name, selected_entity,
                 filter_expression, enriched_fields, cdc_schema_id, last_synced_at)
            VALUES
                (@ChannelId, @SfId, @FullName, @DeveloperName, @SelectedEntity,
                 @FilterExpression, @EnrichedFields::jsonb, @CdcSchemaId, @LastSyncedAt)
            ON CONFLICT (sf_id) DO UPDATE SET
                channel_id = EXCLUDED.channel_id,
                full_name = EXCLUDED.full_name,
                developer_name = EXCLUDED.developer_name,
                selected_entity = EXCLUDED.selected_entity,
                filter_expression = EXCLUDED.filter_expression,
                enriched_fields = EXCLUDED.enriched_fields,
                date_updated = now(),
                last_synced_at = EXCLUDED.last_synced_at
            RETURNING {MemberColumns}";

        var parameters = new {
            member.ChannelId,
            member.SfId,
            member.FullName,
            member.DeveloperName,
            member.SelectedEntity,
            member.FilterExpression,
            member.EnrichedFields,
            member.CdcSchemaId,
            LastSyncedAt = DateTime.UtcNow
        };

        LogQuery("UPSERT", sql, parameters);

        return await connection.QuerySingleAsync<PlatformEventChannelMemberEntity>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    #endregion

    private void LogQuery(string queryType, string sql, object? values = null) {
        if (!_debugQuery) {
            return;
        }
        _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", queryType, sql, values);
    }
}
