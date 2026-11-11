using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Database.Repositories;

public class WritebackFailureRepository : IWritebackFailureRepository {
    private readonly string _connectionString;

    private const string FailureColumns = @"
                id AS Id,
                binding_id AS BindingId,
                row_key AS RowKey,
                writeback_key_value AS WritebackKeyValue,
                salesforce_id AS SalesforceId,
                operation AS Operation,
                status_codes AS StatusCodes,
                message AS Message,
                values_sent::text AS ValuesSent,
                first_failed_at AS FirstFailedAt,
                last_failed_at AS LastFailedAt,
                attempts AS Attempts";

    public WritebackFailureRepository(IConfiguration configuration) {
        _connectionString = configuration.GetConnectionString("appDatabase")
            ?? throw new InvalidOperationException("Db connection string is not configured.");
    }

    public async Task RecordAsync(WritebackFailure failure, CancellationToken cancellationToken = default) {
        const string sql = @"
            INSERT INTO salesforce.writeback_failures (
                binding_id, row_key, writeback_key_value, salesforce_id, operation, status_codes, message,
                values_sent, first_failed_at, last_failed_at, attempts)
            VALUES (
                @BindingId, @RowKey, @WritebackKeyValue, @SalesforceId, @Operation, @StatusCodes, @Message,
                @ValuesSent::jsonb, @At, @At, 1)
            ON CONFLICT (binding_id, row_key) DO UPDATE SET
                writeback_key_value = EXCLUDED.writeback_key_value,
                salesforce_id = EXCLUDED.salesforce_id,
                operation = EXCLUDED.operation,
                status_codes = EXCLUDED.status_codes,
                message = EXCLUDED.message,
                values_sent = EXCLUDED.values_sent,
                last_failed_at = EXCLUDED.last_failed_at,
                attempts = salesforce.writeback_failures.attempts + 1";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new {
            failure.BindingId,
            failure.RowKey,
            failure.WritebackKeyValue,
            failure.SalesforceId,
            Operation = failure.Operation.ToString(),
            failure.StatusCodes,
            failure.Message,
            failure.ValuesSent,
            At = DateTime.SpecifyKind(failure.LastFailedAt, DateTimeKind.Utc)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task ClearAsync(int bindingId, string rowKey, CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM salesforce.writeback_failures WHERE binding_id = @BindingId AND row_key = @RowKey",
            new { BindingId = bindingId, RowKey = rowKey }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<List<WritebackFailure>> ListAsync(int? bindingId = null, CancellationToken cancellationToken = default) {
        var sql = $@"SELECT {FailureColumns} FROM salesforce.writeback_failures
                     WHERE @BindingId::int4 IS NULL OR binding_id = @BindingId
                     ORDER BY last_failed_at DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        return (await connection.QueryAsync<WritebackFailure>(new CommandDefinition(sql,
            new { BindingId = bindingId }, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
    }

    public async Task<WritebackFailure?> GetAsync(int failureId, CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<WritebackFailure>(new CommandDefinition(
            $"SELECT {FailureColumns} FROM salesforce.writeback_failures WHERE id = @Id",
            new { Id = failureId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int failureId, CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM salesforce.writeback_failures WHERE id = @Id",
            new { Id = failureId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM salesforce.writeback_failures", cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
