using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Database.Repositories;

public class InboundFailureRepository : IInboundFailureRepository {
    private readonly string _connectionString;

    private const string FailureColumns = @"
                id AS Id,
                binding_id AS BindingId,
                channel_full_name AS ChannelFullName,
                replay_id AS ReplayId,
                entity_name AS EntityName,
                change_type AS ChangeType,
                record_ids AS RecordIds,
                message AS Message,
                target_table AS TargetTable,
                sql_state AS SqlState,
                target_column AS TargetColumn,
                constraint_name AS ConstraintName,
                values_sent::text AS ValuesSent,
                first_failed_at AS FirstFailedAt,
                last_failed_at AS LastFailedAt,
                attempts AS Attempts";

    public InboundFailureRepository(IConfiguration configuration) {
        _connectionString = configuration.GetConnectionString("appDatabase")
            ?? throw new InvalidOperationException("Db connection string is not configured.");
    }

    public async Task RecordAsync(InboundFailure failure, CancellationToken cancellationToken = default) {
        const string sql = @"
            INSERT INTO salesforce.inbound_failures (
                binding_id, channel_full_name, replay_id, entity_name, change_type, record_ids, message,
                target_table, sql_state, target_column, constraint_name, values_sent,
                first_failed_at, last_failed_at, attempts)
            VALUES (
                @BindingId, @ChannelFullName, @ReplayId, @EntityName, @ChangeType, @RecordIds, @Message,
                @TargetTable, @SqlState, @TargetColumn, @ConstraintName, @ValuesSent::jsonb,
                @FirstFailedAt, @LastFailedAt, 1)
            ON CONFLICT (channel_full_name, replay_id) DO UPDATE SET
                change_type = COALESCE(EXCLUDED.change_type, salesforce.inbound_failures.change_type),
                record_ids = CASE WHEN cardinality(EXCLUDED.record_ids) > 0 THEN EXCLUDED.record_ids
                                  ELSE salesforce.inbound_failures.record_ids END,
                message = EXCLUDED.message,
                target_table = EXCLUDED.target_table,
                sql_state = EXCLUDED.sql_state,
                target_column = EXCLUDED.target_column,
                constraint_name = EXCLUDED.constraint_name,
                values_sent = EXCLUDED.values_sent,
                last_failed_at = EXCLUDED.last_failed_at,
                attempts = salesforce.inbound_failures.attempts + 1";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new {
            failure.BindingId,
            failure.ChannelFullName,
            failure.ReplayId,
            failure.EntityName,
            failure.ChangeType,
            failure.RecordIds,
            failure.Message,
            failure.TargetTable,
            failure.SqlState,
            failure.TargetColumn,
            failure.ConstraintName,
            failure.ValuesSent,
            FirstFailedAt = DateTime.SpecifyKind(failure.FirstFailedAt, DateTimeKind.Utc),
            LastFailedAt = DateTime.SpecifyKind(failure.LastFailedAt, DateTimeKind.Utc)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<List<InboundFailure>> ListAsync(int? bindingId = null, CancellationToken cancellationToken = default) {
        var sql = $@"SELECT {FailureColumns} FROM salesforce.inbound_failures
                     WHERE @BindingId::int4 IS NULL OR binding_id = @BindingId
                     ORDER BY last_failed_at DESC, id DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        return (await connection.QueryAsync<InboundFailure>(new CommandDefinition(sql,
            new { BindingId = bindingId }, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
    }

    public async Task DeleteAsync(int failureId, CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM salesforce.inbound_failures WHERE id = @Id",
            new { Id = failureId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<Dictionary<int, int>> CountByBindingAsync(CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<(int BindingId, int Count)>(new CommandDefinition(
            "SELECT binding_id, count(*)::int FROM salesforce.inbound_failures GROUP BY binding_id",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToDictionary(r => r.BindingId, r => r.Count);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM salesforce.inbound_failures", cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
