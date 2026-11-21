using Database.Repositories;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Collections.Concurrent;

namespace Database.Writeback;

/// <summary>
/// A Postgres repository whose every write carries this application's replication origin, so Writeback skips it.
/// </summary>
/// <remarks>
/// A replication origin can be set up in only one session at a time, so every tagged repository for a database
/// shares one data source holding a single connection, set up with the origin when it is opened and kept for as
/// long as the process runs. Concurrent writes queue for it; writes are short, and this is the price of one tag.
/// Shared across instances because the Target Connection provider builds a new repository each time it is
/// invalidated, and two pools would each try to hold the origin.
/// </remarks>
public sealed class TaggedPostgresRepository : PostgresRepository {
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new(StringComparer.Ordinal);

    public TaggedPostgresRepository(ILogger<RepositoryBase> logger, string connectionString, bool debugQuery)
        : base(logger, connectionString, debugQuery, DataSources.GetOrAdd(connectionString, Build)) { }

    private static NpgsqlDataSource Build(string connectionString) {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.ConnectionStringBuilder.MaxPoolSize = 1;
        builder.ConnectionStringBuilder.ApplicationName = "StarPush (Writeback-tagged)";
        builder.UsePhysicalConnectionInitializer(connection => SetUpOrigin(connection).GetAwaiter().GetResult(), SetUpOrigin);
        return builder.Build();
    }

    private static async Task SetUpOrigin(NpgsqlConnection connection) {
        await using var command = new NpgsqlCommand("SELECT pg_replication_origin_session_setup(@Origin)", connection);
        command.Parameters.AddWithValue("Origin", PostgresWriteback.OriginName);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
