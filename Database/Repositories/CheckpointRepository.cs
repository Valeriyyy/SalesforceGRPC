using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Repositories;

/// <summary>
/// Dapper-backed store for Checkpoints. Always talks to the App Database (ConnectionStrings:appDatabase),
/// like the other metadata repositories.
/// </summary>
public class CheckpointRepository : ICheckpointRepository {
    private readonly ILogger<CheckpointRepository> _logger;
    private readonly string _connectionString;
    private readonly bool _debugQuery;

    public CheckpointRepository(ILogger<CheckpointRepository> logger, IConfiguration configuration) {
        _logger = logger;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
        _connectionString = configuration.GetConnectionString("appDatabase")
            ?? throw new InvalidOperationException("Db connection string is not configured.");
    }

    public async Task<Checkpoint?> GetAsync(int channelId, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT channel_id AS ChannelId, replay_id AS ReplayId, saved_at AS SavedAt
            FROM salesforce.channel_checkpoints
            WHERE channel_id = @ChannelId";

        LogQuery("SELECT", sql, channelId);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<Checkpoint>(
            new CommandDefinition(sql, new { ChannelId = channelId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SaveAsync(int channelId, byte[] replayId, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(replayId);

        const string sql = @"
            INSERT INTO salesforce.channel_checkpoints (channel_id, replay_id, saved_at)
            VALUES (@ChannelId, @ReplayId, now())
            ON CONFLICT (channel_id) DO UPDATE SET
                replay_id = EXCLUDED.replay_id,
                saved_at = EXCLUDED.saved_at";

        LogQuery("UPSERT", sql, channelId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { ChannelId = channelId, ReplayId = replayId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int channelId, CancellationToken cancellationToken = default) {
        const string sql = "DELETE FROM salesforce.channel_checkpoints WHERE channel_id = @ChannelId";

        LogQuery("DELETE", sql, channelId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { ChannelId = channelId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private void LogQuery(string queryType, string sql, int channelId) {
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, ChannelId: {ChannelId}", queryType, sql, channelId);
        }
    }
}
