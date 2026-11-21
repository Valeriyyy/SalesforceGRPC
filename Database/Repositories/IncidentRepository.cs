using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Database.Repositories;

public class IncidentRepository : IIncidentRepository {
    private readonly string _connectionString;

    private const string IncidentColumns = @"
                id AS Id,
                direction AS Direction,
                kind AS Kind,
                message AS Message,
                started_at AS StartedAt,
                ended_at AS EndedAt,
                occurrences AS Occurrences";

    public IncidentRepository(IConfiguration configuration) {
        _connectionString = configuration.GetConnectionString("appDatabase")
            ?? throw new InvalidOperationException("Db connection string is not configured.");
    }

    public async Task<int> OpenAsync(Incident incident, CancellationToken cancellationToken = default) {
        // Pruned here rather than on a timer: Incidents arrive rarely, and only when one does can the list grow.
        const string sql = @"
            DELETE FROM salesforce.incidents
             WHERE ended_at IS NOT NULL AND ended_at < @StartedAt - make_interval(secs => @RetentionSeconds);
            INSERT INTO salesforce.incidents (direction, kind, message, started_at, ended_at, occurrences)
            VALUES (@Direction, @Kind, @Message, @StartedAt, @EndedAt, 1)
            RETURNING id;";

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new {
            Direction = incident.Direction?.ToString(),
            Kind = incident.Kind.ToString(),
            incident.Message,
            StartedAt = Utc(incident.StartedAt),
            EndedAt = incident.EndedAt is { } ended ? Utc(ended) : (DateTime?)null,
            RetentionSeconds = IIncidentRepository.Retention.TotalSeconds
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task ContinueAsync(int incidentId, string message, CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE salesforce.incidents
               SET occurrences = occurrences + 1, message = @Message, ended_at = NULL
             WHERE id = @Id",
            new { Id = incidentId, Message = message }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task CloseAsync(int incidentId, DateTime endedAt, CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE salesforce.incidents SET ended_at = @EndedAt WHERE id = @Id",
            new { Id = incidentId, EndedAt = Utc(endedAt) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task CloseAllOpenAsync(DateTime endedAt, CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE salesforce.incidents SET ended_at = @EndedAt WHERE ended_at IS NULL",
            new { EndedAt = Utc(endedAt) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<List<Incident>> ListSinceAsync(DateTime since, CancellationToken cancellationToken = default) {
        var sql = $@"SELECT {IncidentColumns} FROM salesforce.incidents
                     WHERE started_at >= @Since OR ended_at IS NULL OR ended_at >= @Since
                     ORDER BY started_at DESC, id DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        return (await connection.QueryAsync<Incident>(new CommandDefinition(sql,
            new { Since = Utc(since) }, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
    }

    public async Task<IncidentKind?> GetLatestLifecycleKindAsync(CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        var kind = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(@"
            SELECT kind FROM salesforce.incidents
             WHERE kind IN ('Started', 'Stopped')
             ORDER BY started_at DESC, id DESC
             LIMIT 1", cancellationToken: cancellationToken)).ConfigureAwait(false);
        return kind is null ? null : Enum.Parse<IncidentKind>(kind);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
