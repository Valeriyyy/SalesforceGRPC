using Dapper;
using Database.Repositories;
using Database.Writeback;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace StarPush.Tests;

/// <summary>
/// Postgres Writeback against a real server: arming, reading other sessions' changes, skipping this
/// application's own tagged writes, resuming after a restart, and disarming.
/// </summary>
/// <remarks>
/// Runs only when <c>STARPUSH_TEST_TARGET_DATABASE</c> points at Postgres 16 or later with
/// <c>wal_level = logical</c>, and skips otherwise. Point it at a throwaway database: the tests create and drop
/// the publication and slot Writeback uses, which are named the same in every database. The tests in this class
/// run one after another, as they share that slot.
/// </remarks>
[Collection(PostgresTargetDatabase.Name)]
public class PostgresWritebackTests {
    private const string Variable = "STARPUSH_TEST_TARGET_DATABASE";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Scratch : IAsyncDisposable {
        public required string ConnectionString { get; init; }
        public required WritebackTable Table { get; init; }
        public required PostgresWriteback Writeback { get; init; }
        public WritebackEndpoints Endpoints => new(ConnectionString, ConnectionString);
        public string QualifiedName => $"{Schema}.{Table.Table}";

        /// <summary>
        /// A schema of their own, so the tables these tests create and drop never race other tests that list
        /// <c>public</c> against the same database.
        /// </summary>
        public const string Schema = "writeback_test";

        public static async Task<Scratch> CreateAsync() {
            var connectionString = Environment.GetEnvironmentVariable(Variable);
            Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString), $"Set {Variable} to a Postgres connection string to run this test.");

            await using var connection = new NpgsqlConnection(connectionString);
            var (version, walLevel) = await connection.QuerySingleAsync<(int, string)>(
                "SELECT current_setting('server_version_num')::int, current_setting('wal_level')");
            Assert.SkipWhen(version < 160000 || walLevel != "logical",
                $"Writeback needs Postgres 16+ with wal_level = logical; {Variable} has version {version} and wal_level '{walLevel}'.");

            var table = new WritebackTable(Schema, $"writeback_test_{Guid.NewGuid():N}"[..30]);
            await connection.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS {Schema}");
            await connection.ExecuteAsync(
                $"CREATE TABLE {Schema}.{table.Table} (id int PRIMARY KEY, sf_id varchar(18) UNIQUE, phone varchar(40))");

            return new Scratch {
                ConnectionString = connectionString!,
                Table = table,
                Writeback = new PostgresWriteback(NullLoggerFactory.Instance)
            };
        }

        public async Task ExecuteAsync(string sql) {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.ExecuteAsync(sql);
        }

        public async ValueTask DisposeAsync() {
            await Writeback.DisarmAsync(Endpoints);
            await ExecuteAsync($"DROP TABLE IF EXISTS {QualifiedName}");
        }
    }

    private static async Task<ChangeTransaction> NextAsync(IAsyncEnumerator<ChangeTransaction> changes) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var next = changes.MoveNextAsync().AsTask();
        await next.WaitAsync(timeout.Token);
        Assert.True(next.Result, "The change stream ended.");
        return changes.Current;
    }

    [Fact]
    public async Task OtherSessionsChangesArrive_WithTheOldRow_ButThisApplicationsTaggedWritesDoNot() {
        await using var scratch = await Scratch.CreateAsync();
        await scratch.Writeback.ArmAsync(scratch.Endpoints, [scratch.Table], Ct);
        var tagged = scratch.Writeback.CreateTaggedRepository(scratch.ConnectionString);

        await scratch.ExecuteAsync($"INSERT INTO {scratch.QualifiedName} (id, phone) VALUES (7, '111')");
        await tagged.UpdateByKey(scratch.QualifiedName, "id", 7, new Dictionary<string, object?> { ["sf_id"] = "001000000000000AAA" }, Ct);
        // A second write reuses the pooled connection, which must still carry the tag after being returned.
        await tagged.UpdateByKey(scratch.QualifiedName, "id", 7, new Dictionary<string, object?> { ["sf_id"] = "001000000000001AAA" }, Ct);
        await scratch.ExecuteAsync($"UPDATE {scratch.QualifiedName} SET phone = '222' WHERE id = 7");

        await using var stream = await scratch.Writeback.OpenStreamAsync(scratch.Endpoints, Ct);
        await using var changes = stream.ReadAsync(Ct).GetAsyncEnumerator(Ct);

        var insert = Assert.Single((await NextAsync(changes)).Changes);
        Assert.Equal(RowChangeKind.Insert, insert.Kind);
        Assert.Equal(scratch.Table, insert.Table);
        Assert.Equal(7, insert.NewValues["id"]);
        Assert.Equal("111", insert.NewValues["phone"]);

        // Both tagged writes of the Salesforce ID are skipped; the next transaction is the other session's update.
        var update = Assert.Single((await NextAsync(changes)).Changes);
        Assert.Equal(RowChangeKind.Update, update.Kind);
        Assert.Equal("111", update.OldValues!["phone"]);
        Assert.Equal("222", update.NewValues["phone"]);
        Assert.Equal("001000000000001AAA", update.NewValues["sf_id"]);
    }

    [Fact]
    public async Task ConcurrentTaggedWrites_AllSucceed_EvenFromTwoRepositories_AndNoneArrive() {
        await using var scratch = await Scratch.CreateAsync();
        await scratch.Writeback.ArmAsync(scratch.Endpoints, [scratch.Table], Ct);
        for (var id = 1; id <= 8; id++) {
            await scratch.ExecuteAsync($"INSERT INTO {scratch.QualifiedName} (id, phone) VALUES ({id}, 'x')");
        }

        // The inbound flow writes from parallel ordering groups, and the provider builds a fresh repository each
        // time it is invalidated; a replication origin can be active in only one session at a time.
        var first = scratch.Writeback.CreateTaggedRepository(scratch.ConnectionString);
        var second = scratch.Writeback.CreateTaggedRepository(scratch.ConnectionString);
        await Task.WhenAll(Enumerable.Range(1, 8).Select(id => (id % 2 == 0 ? first : second).UpdateByKey(
            scratch.QualifiedName, "id", id, new Dictionary<string, object?> { ["phone"] = $"tagged {id}" }, Ct)));
        await scratch.ExecuteAsync($"UPDATE {scratch.QualifiedName} SET phone = 'untagged' WHERE id = 1");

        await using var stream = await scratch.Writeback.OpenStreamAsync(scratch.Endpoints, Ct);
        await using var changes = stream.ReadAsync(Ct).GetAsyncEnumerator(Ct);
        for (var i = 0; i < 8; i++) {
            Assert.Equal(RowChangeKind.Insert, Assert.Single((await NextAsync(changes)).Changes).Kind);
        }
        var next = Assert.Single((await NextAsync(changes)).Changes);
        Assert.Equal("untagged", next.NewValues["phone"]);
    }

    [Fact]
    public async Task ReopeningTheStream_ResumesAfterTheLastConfirmedTransaction() {
        await using var scratch = await Scratch.CreateAsync();
        await scratch.Writeback.ArmAsync(scratch.Endpoints, [scratch.Table], Ct);

        await scratch.ExecuteAsync($"INSERT INTO {scratch.QualifiedName} (id, phone) VALUES (1, 'first')");
        await scratch.ExecuteAsync($"INSERT INTO {scratch.QualifiedName} (id, phone) VALUES (2, 'second')");

        await using (var stream = await scratch.Writeback.OpenStreamAsync(scratch.Endpoints, Ct)) {
            await using var changes = stream.ReadAsync(Ct).GetAsyncEnumerator(Ct);
            var first = await NextAsync(changes);
            await stream.ConfirmAsync(first.Position, Ct);
        }

        await using var reopened = await scratch.Writeback.OpenStreamAsync(scratch.Endpoints, Ct);
        await using var resumed = reopened.ReadAsync(Ct).GetAsyncEnumerator(Ct);
        var next = Assert.Single((await NextAsync(resumed)).Changes);
        Assert.Equal("second", next.NewValues["phone"]);
    }

    [Fact]
    public async Task Disarming_DropsTheSlotAndPublication_SoTheServerStopsKeepingChanges() {
        await using var scratch = await Scratch.CreateAsync();
        await scratch.Writeback.ArmAsync(scratch.Endpoints, [scratch.Table], Ct);

        await scratch.Writeback.DisarmAsync(scratch.Endpoints, Ct);

        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = '{PostgresWriteback.SlotName}')"));
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = '{PostgresWriteback.PublicationName}')"));
        Assert.Null(await scratch.Writeback.GetLagBytesAsync(scratch.Endpoints, Ct));
    }

    [Fact]
    public async Task Disarming_WhileAStreamIsStillReading_EndsThatReaderAndStillDropsTheSlot() {
        await using var scratch = await Scratch.CreateAsync();
        await scratch.Writeback.ArmAsync(scratch.Endpoints, [scratch.Table], Ct);
        await scratch.ExecuteAsync($"INSERT INTO {scratch.QualifiedName} (id, phone) VALUES (1, 'first')");

        // A Repoint or Disconnect can land while the Writeback worker is mid-stream.
        await using var stream = await scratch.Writeback.OpenStreamAsync(scratch.Endpoints, Ct);
        await using var changes = stream.ReadAsync(Ct).GetAsyncEnumerator(Ct);
        await NextAsync(changes);

        await scratch.Writeback.DisarmAsync(scratch.Endpoints, Ct);

        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = '{PostgresWriteback.SlotName}')"));
    }

    [Fact]
    public async Task ArmingATableItAlreadyHas_ChangesNothing_AndTheTableSendsItsWholeOldRow() {
        await using var scratch = await Scratch.CreateAsync();
        await scratch.Writeback.ArmAsync(scratch.Endpoints, [scratch.Table], Ct);
        await scratch.Writeback.ArmAsync(scratch.Endpoints, [scratch.Table], Ct);

        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        Assert.Equal("f", await connection.ExecuteScalarAsync<string>(
            $"SELECT relreplident::text FROM pg_class WHERE oid = '{scratch.QualifiedName}'::regclass"));
        Assert.Empty(await scratch.Writeback.CheckPrerequisitesAsync(scratch.Endpoints, [scratch.Table], Ct));
    }
}

/// <summary>
/// Tests that create, drop or list tables in the Postgres test database run one class at a time, so one never
/// lists a table another is about to drop.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresTargetDatabase {
    public const string Name = "Postgres target database";
}

