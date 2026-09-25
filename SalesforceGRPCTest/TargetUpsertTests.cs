using Dapper;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Npgsql;
using System.Data.Common;

namespace SalesforceGRPCTest;

/// <summary>
/// The upsert every CREATE is written with, against each engine's real database.
/// </summary>
/// <remarks>
/// Delivery is at-least-once (ADR 0005), so a CREATE can arrive twice and must leave one row. SQLite runs
/// unconditionally against a temporary file. The other engines read the same environment variables as the
/// <c>*MetadataRepositoryTests</c> classes and skip when theirs is unset.
/// </remarks>
public class TargetUpsertTests {
    public static TheoryData<TargetDatabaseEngine> Engines => [
        TargetDatabaseEngine.Postgres, TargetDatabaseEngine.SqlServer, TargetDatabaseEngine.MySql, TargetDatabaseEngine.Sqlite
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task UpsertingTheSameRecordTwice_LeavesOneRowHoldingTheSecondValues(TargetDatabaseEngine engine) {
        await using var target = await ScratchTable.CreateAsync(engine);

        await target.Repository.Upsert(target.Table, "sf_id",
            new Dictionary<string, object> { ["sf_id"] = "001A", ["phone"] = "555-0100", ["name"] = "First" }, Ct);
        await target.Repository.Upsert(target.Table, "sf_id",
            new Dictionary<string, object> { ["sf_id"] = "001A", ["phone"] = "555-0199", ["name"] = "Second" }, Ct);

        var rows = await target.RowsAsync();
        var row = Assert.Single(rows);
        Assert.Equal(("001A", "555-0199", "Second"), row);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task UpsertingARecordWithNothingButItsKey_Twice_LeavesOneRow(TargetDatabaseEngine engine) {
        await using var target = await ScratchTable.CreateAsync(engine);

        await target.Repository.Upsert(target.Table, "sf_id", new Dictionary<string, object> { ["sf_id"] = "001A" }, Ct);
        await target.Repository.Upsert(target.Table, "sf_id", new Dictionary<string, object> { ["sf_id"] = "001A" }, Ct);

        Assert.Single(await target.RowsAsync());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task UpsertingDifferentRecords_AddsARowForEach(TargetDatabaseEngine engine) {
        await using var target = await ScratchTable.CreateAsync(engine);

        await target.Repository.Upsert(target.Table, "sf_id",
            new Dictionary<string, object> { ["sf_id"] = "001A", ["phone"] = "555-0100", ["name"] = "A" }, Ct);
        await target.Repository.Upsert(target.Table, "sf_id",
            new Dictionary<string, object> { ["sf_id"] = "001B", ["phone"] = "555-0200", ["name"] = "B" }, Ct);

        Assert.Equal(["001A", "001B"], (await target.RowsAsync()).Select(r => r.SfId));
    }

    /// <summary>A throwaway table with a unique Key Mapping column, dropped afterwards.</summary>
    private sealed class ScratchTable : IAsyncDisposable {
        private readonly Func<DbConnection> _connect;
        private readonly string? _sqliteFile;

        public IRepository Repository { get; }
        public string Table { get; }

        private ScratchTable(IRepository repository, string table, Func<DbConnection> connect, string? sqliteFile) {
            Repository = repository;
            Table = table;
            _connect = connect;
            _sqliteFile = sqliteFile;
        }

        public static async Task<ScratchTable> CreateAsync(TargetDatabaseEngine engine) {
            var name = $"upsert_test_{Guid.NewGuid():N}"[..28];
            var target = engine switch {
                TargetDatabaseEngine.Postgres => Server("SALESFORCEGRPC_TEST_TARGET_DATABASE", engine, name,
                    cs => new PostgresRepository(NullLogger<RepositoryBase>.Instance, cs, false), cs => new NpgsqlConnection(cs)),
                TargetDatabaseEngine.SqlServer => Server("SALESFORCEGRPC_TEST_SQLSERVER_TARGET_DATABASE", engine, name,
                    cs => new SqlServerRepository(NullLogger<SqlServerRepository>.Instance, cs, false), cs => new SqlConnection(cs)),
                TargetDatabaseEngine.MySql => Server("SALESFORCEGRPC_TEST_MYSQL_TARGET_DATABASE", engine, name,
                    cs => new MySqlRepository(NullLogger<MySqlRepository>.Instance, cs, false), cs => new MySqlConnection(cs)),
                _ => Sqlite(name)
            };

            await using var connection = target._connect();
            await connection.ExecuteAsync(
                $"CREATE TABLE {target.Table} (sf_id varchar(18) NOT NULL UNIQUE, phone varchar(40) NULL, name varchar(80) NULL)");
            return target;
        }

        private static ScratchTable Server(string variable, TargetDatabaseEngine engine, string table,
            Func<string, IRepository> repository, Func<string, DbConnection> connect) {
            var connectionString = Environment.GetEnvironmentVariable(variable);
            Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
                $"Set {variable} to a {engine} connection string to run this test.");
            return new ScratchTable(repository(connectionString!), table, () => connect(connectionString!), null);
        }

        private static ScratchTable Sqlite(string table) {
            var file = Path.Combine(Path.GetTempPath(), $"{table}.db");
            var connectionString = new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString();
            return new ScratchTable(new SqliteRepository(NullLogger<RepositoryBase>.Instance, connectionString, false),
                table, () => new SqliteConnection(connectionString), file);
        }

        public async Task<List<(string SfId, string? Phone, string? Name)>> RowsAsync() {
            await using var connection = _connect();
            var rows = await connection.QueryAsync<(string, string?, string?)>(
                $"SELECT sf_id, phone, name FROM {Table} ORDER BY sf_id");
            return rows.ToList();
        }

        public async ValueTask DisposeAsync() {
            if (_sqliteFile is not null) {
                File.Delete(_sqliteFile);
                return;
            }

            await using var connection = _connect();
            await connection.ExecuteAsync($"DROP TABLE {Table}");
        }
    }
}
