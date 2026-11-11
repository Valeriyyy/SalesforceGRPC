using Dapper;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;
using NpgsqlTypes;
using System.Runtime.CompilerServices;

namespace Database.Writeback;

/// <summary>
/// Writeback for Postgres: logical replication through <c>pgoutput</c>, read with the Replication Credential.
/// </summary>
/// <remarks>
/// <para>
/// Arming creates a replication origin (<see cref="OriginName"/>), a publication over exactly the Two-way
/// Target Tables, sets <c>REPLICA IDENTITY FULL</c> on them so an UPDATE carries the old row, and creates the
/// slot that holds Writeback's position. This changes table settings, never columns or data (ADR 0007).
/// </para>
/// <para>
/// Every write this application makes to an armed database runs in a session set up with the origin, so its
/// transactions arrive tagged with it and are skipped. Npgsql 10 does not pass <c>pgoutput</c>'s
/// <c>origin = none</c>, so the filtering is done here, on the Origin message that precedes such a transaction's
/// changes. Two-way still requires Postgres 16, the version the user chose to support.
/// </para>
/// </remarks>
public sealed class PostgresWriteback : IWritebackCapability {
    /// <summary>Tags this application's writes. Stored transactions carry it, so it must not change.</summary>
    public const string OriginName = "starpush";

    public const string PublicationName = "starpush_writeback";
    public const string SlotName = "starpush_writeback";

    /// <summary>Postgres 16, as server_version_num reports it.</summary>
    private const int MinimumServerVersion = 160000;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PostgresWriteback> _logger;
    private readonly bool _debugQuery;

    public PostgresWriteback(ILoggerFactory loggerFactory, bool debugQuery = false) {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PostgresWriteback>();
        _debugQuery = debugQuery;
    }

    public async Task<string?> GetUnavailableReasonAsync(string connectionString, CancellationToken cancellationToken = default) {
        await using var connection = await OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        return TooOld(await ServerVersion(connection, cancellationToken).ConfigureAwait(false));
    }

    private static string? TooOld(int serverVersion) => serverVersion >= MinimumServerVersion
        ? null
        : $"Postgres {serverVersion / 10000} is too old for Two-way; it needs Postgres 16 or later. Inbound still works.";

    public async Task<IReadOnlyList<WritebackPrerequisite>> CheckPrerequisitesAsync(WritebackEndpoints endpoints,
        IReadOnlyList<WritebackTable> tables, CancellationToken cancellationToken = default) {
        var unmet = new List<WritebackPrerequisite>();

        await using (var connection = await OpenAsync(endpoints.ConnectionString, cancellationToken).ConfigureAwait(false)) {
            var server = await connection.QuerySingleAsync<ServerFacts>(new CommandDefinition(@"
                SELECT current_setting('wal_level') AS WalLevel,
                       current_setting('max_replication_slots')::int AS MaxSlots,
                       (SELECT count(*)::int FROM pg_replication_slots) AS UsedSlots,
                       EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = @Slot) AS HasSlot,
                       EXISTS (SELECT 1 FROM pg_publication WHERE pubname = @Publication) AS HasPublication,
                       EXISTS (SELECT 1 FROM pg_replication_origin WHERE roname = @Origin) AS HasOrigin,
                       current_user AS UserName,
                       (SELECT rolsuper FROM pg_roles WHERE rolname = current_user) AS IsSuperuser,
                       has_function_privilege('pg_replication_origin_session_setup(text)', 'EXECUTE') AS CanTag,
                       has_function_privilege('pg_replication_origin_create(text)', 'EXECUTE') AS CanCreateOrigin,
                       has_database_privilege(current_database(), 'CREATE') AS CanCreatePublication",
                new { Slot = SlotName, Publication = PublicationName, Origin = OriginName },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (server.WalLevel != "logical") {
                unmet.Add(new WritebackPrerequisite(
                    $"The server's wal_level is '{server.WalLevel}'. Writeback reads changes through logical replication, which needs 'logical'; changing it takes a server restart.",
                    "ALTER SYSTEM SET wal_level = logical;  -- then restart Postgres"));
            }

            if (!server.HasSlot && server.UsedSlots >= server.MaxSlots) {
                unmet.Add(new WritebackPrerequisite(
                    $"Every replication slot is in use ({server.UsedSlots} of {server.MaxSlots}), and Writeback needs one of its own.",
                    $"ALTER SYSTEM SET max_replication_slots = {server.MaxSlots + 1};  -- then restart Postgres"));
            }

            var user = Quote(server.UserName);
            if (!server.IsSuperuser && !server.CanTag) {
                unmet.Add(new WritebackPrerequisite(
                    $"The Target Connection's login '{server.UserName}' cannot tag its own writes, so Writeback could not tell them apart from everyone else's and would send them back to Salesforce.",
                    $"GRANT EXECUTE ON FUNCTION pg_replication_origin_session_setup(text) TO {user};"));
            }
            if (!server.HasOrigin && !server.IsSuperuser && !server.CanCreateOrigin) {
                unmet.Add(new WritebackPrerequisite(
                    $"The replication origin '{OriginName}' that tags this application's writes does not exist, and '{server.UserName}' cannot create it.",
                    $"SELECT pg_replication_origin_create('{OriginName}');"));
            }

            foreach (var table in tables) {
                var facts = await TableFacts(connection, table, cancellationToken).ConfigureAwait(false);
                if (facts is null) {
                    unmet.Add(new WritebackPrerequisite($"Target Table {table} does not exist."));
                    continue;
                }
                if (facts.CanAlter) {
                    continue;
                }

                // Not the owner, so arming cannot set the table up itself: whatever is not already in place has
                // to be done by someone who can.
                if (facts.ReplicaIdentity != "f") {
                    unmet.Add(new WritebackPrerequisite(
                        $"{table} must send its whole old row with every change, so Writeback can tell which fields changed, and '{server.UserName}' does not own it to set that.",
                        $"ALTER TABLE {QualifiedName(table)} REPLICA IDENTITY FULL;"));
                }
                if (!facts.InPublication) {
                    unmet.Add(new WritebackPrerequisite(
                        $"{table} is not in the publication Writeback reads, and '{server.UserName}' does not own it to add it.",
                        server.HasPublication
                            ? $"ALTER PUBLICATION {PublicationName} ADD TABLE {QualifiedName(table)};"
                            : $"CREATE PUBLICATION {PublicationName} FOR TABLE {QualifiedName(table)};"));
                }
            }

            if (!server.HasPublication && !server.IsSuperuser && !server.CanCreatePublication) {
                unmet.Add(new WritebackPrerequisite(
                    $"'{server.UserName}' cannot create the publication Writeback reads in this database.",
                    $"GRANT CREATE ON DATABASE {Quote(connection.Database)} TO {user};"));
            }
        }

        await using (var replication = await OpenAsync(endpoints.ReplicationConnectionString, cancellationToken).ConfigureAwait(false)) {
            var role = await replication.QuerySingleAsync<(string Name, bool CanReplicate)>(new CommandDefinition(
                "SELECT current_user, rolreplication OR rolsuper FROM pg_roles WHERE rolname = current_user",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!role.CanReplicate) {
                unmet.Add(new WritebackPrerequisite(
                    $"The Replication Credential's login '{role.Name}' may not read the database's changes.",
                    $"ALTER ROLE {Quote(role.Name)} WITH REPLICATION;"));
            }

            foreach (var table in tables) {
                var canRead = await replication.ExecuteScalarAsync<bool?>(new CommandDefinition(
                    "SELECT has_table_privilege(to_regclass(@Table), 'SELECT')",
                    new { Table = QualifiedName(table) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (canRead == false) {
                    unmet.Add(new WritebackPrerequisite(
                        $"The Replication Credential's login '{role.Name}' cannot read {table}.",
                        $"GRANT SELECT ON {QualifiedName(table)} TO {Quote(role.Name)};"));
                }
            }
        }

        return unmet;
    }

    public async Task ArmAsync(WritebackEndpoints endpoints, IReadOnlyList<WritebackTable> tables,
        CancellationToken cancellationToken = default) {
        await using (var connection = await OpenAsync(endpoints.ConnectionString, cancellationToken).ConfigureAwait(false)) {
            await Execute(connection, @"
                SELECT pg_replication_origin_create(@Origin)
                WHERE NOT EXISTS (SELECT 1 FROM pg_replication_origin WHERE roname = @Origin)",
                new { Origin = OriginName }, cancellationToken).ConfigureAwait(false);

            foreach (var table in tables) {
                var facts = await TableFacts(connection, table, cancellationToken).ConfigureAwait(false);
                if (facts is { ReplicaIdentity: not "f", CanAlter: true }) {
                    await Execute(connection, $"ALTER TABLE {QualifiedName(table)} REPLICA IDENTITY FULL", null, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var published = (await connection.QueryAsync<(string Schema, string Table)>(new CommandDefinition(
                    "SELECT schemaname, tablename FROM pg_publication_tables WHERE pubname = @Publication",
                    new { Publication = PublicationName }, cancellationToken: cancellationToken)).ConfigureAwait(false))
                .Select(t => new WritebackTable(t.Schema, t.Table))
                .ToHashSet();
            var hasPublication = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = @Publication)",
                new { Publication = PublicationName }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            var list = string.Join(", ", tables.Select(QualifiedName));
            if (!hasPublication) {
                await Execute(connection, $"CREATE PUBLICATION {PublicationName} FOR TABLE {list}", null, cancellationToken)
                    .ConfigureAwait(false);
            } else if (!published.SetEquals(tables)) {
                await Execute(connection, $"ALTER PUBLICATION {PublicationName} SET TABLE {list}", null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Last, so that by the time the slot starts keeping changes, everything that tags and filters them exists.
        await using var replication = await OpenAsync(endpoints.ReplicationConnectionString, cancellationToken).ConfigureAwait(false);
        await Execute(replication, @"
            SELECT pg_create_logical_replication_slot(@Slot, 'pgoutput')
            WHERE NOT EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = @Slot)",
            new { Slot = SlotName }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Armed Writeback for {Tables}", string.Join(", ", tables));
    }

    public async Task DisarmAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default) {
        await using (var replication = await OpenAsync(endpoints.ReplicationConnectionString, cancellationToken).ConfigureAwait(false)) {
            // A slot being read cannot be dropped. A Repoint or Disconnect can land while the Writeback worker is
            // mid-stream, so its reader — running as this same login — is ended first, and the drop retried
            // while the server lets go of the slot.
            for (var attempt = 1; ; attempt++) {
                await Execute(replication, @"
                    SELECT pg_terminate_backend(active_pid) FROM pg_replication_slots
                    WHERE slot_name = @Slot AND active_pid IS NOT NULL",
                    new { Slot = SlotName }, cancellationToken).ConfigureAwait(false);
                try {
                    await Execute(replication, @"
                        SELECT pg_drop_replication_slot(@Slot)
                        WHERE EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = @Slot)",
                        new { Slot = SlotName }, cancellationToken).ConfigureAwait(false);
                    break;
                } catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ObjectInUse && attempt < 20) {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // The origin is kept: pooled connections may still be set up with it, and it costs nothing. Replica
        // identity is left as it is, since it changes nothing about the table's data.
        await using var connection = await OpenAsync(endpoints.ConnectionString, cancellationToken).ConfigureAwait(false);
        await Execute(connection, $"DROP PUBLICATION IF EXISTS {PublicationName}", null, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Disarmed Writeback: dropped the replication slot and publication");
    }

    public async Task<IWritebackChangeStream> OpenStreamAsync(WritebackEndpoints endpoints,
        CancellationToken cancellationToken = default) {
        var connection = new LogicalReplicationConnection(endpoints.ReplicationConnectionString);
        try {
            await connection.Open(cancellationToken).ConfigureAwait(false);
        } catch {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return new Stream(connection);
    }

    public IRepository CreateTaggedRepository(string connectionString) =>
        new TaggedPostgresRepository(_loggerFactory.CreateLogger<TaggedPostgresRepository>(), connectionString, _debugQuery);

    public async Task<long?> GetLagBytesAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default) {
        await using var connection = await OpenAsync(endpoints.ReplicationConnectionString, cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(@"
            SELECT pg_wal_lsn_diff(pg_current_wal_lsn(), confirmed_flush_lsn)::bigint
            FROM pg_replication_slots WHERE slot_name = @Slot",
            new { Slot = SlotName }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<string?> ProveReplicationCredentialAsync(string replicationConnectionString,
        CancellationToken cancellationToken = default) {
        try {
            await using var connection = await OpenAsync(replicationConnectionString, cancellationToken).ConfigureAwait(false);
            if (TooOld(await ServerVersion(connection, cancellationToken).ConfigureAwait(false)) is { } tooOld) {
                return tooOld;
            }

            var role = await connection.QuerySingleAsync<(string Name, bool CanReplicate)>(new CommandDefinition(
                "SELECT current_user, rolreplication OR rolsuper FROM pg_roles WHERE rolname = current_user",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return role.CanReplicate
                ? null
                : $"The login '{role.Name}' may not read the database's changes. Run: ALTER ROLE {Quote(role.Name)} WITH REPLICATION;";
        } catch (PostgresException ex) {
            return $"The Replication Credential could not log in: {ex.MessageText}";
        } catch (NpgsqlException ex) {
            return $"The Target Database could not be reached with the Replication Credential: {ex.Message}";
        }
    }

    #region Reading changes

    private sealed class Stream : IWritebackChangeStream {
        private readonly LogicalReplicationConnection _connection;

        public Stream(LogicalReplicationConnection connection) => _connection = connection;

        public async IAsyncEnumerable<ChangeTransaction> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var slot = new PgOutputReplicationSlot(SlotName);
            var options = new PgOutputReplicationOptions(PublicationName, PgOutputProtocolVersion.V1, binary: true);

            List<RowChange>? changes = null;
            var ours = false;

            await foreach (var message in _connection.StartReplication(slot, options, cancellationToken).ConfigureAwait(false)) {
                switch (message) {
                    case BeginMessage:
                        changes = [];
                        ours = false;
                        break;

                    case OriginMessage origin:
                        // This application's own write, tagged by its session: never sent back.
                        ours = origin.OriginName == OriginName;
                        break;

                    case InsertMessage insert:
                        changes?.Add(new RowChange {
                            Table = TableOf(insert.Relation),
                            Kind = RowChangeKind.Insert,
                            NewValues = (await ReadRow(insert.NewRow, insert.Relation, cancellationToken).ConfigureAwait(false)).Values
                        });
                        break;

                    case FullUpdateMessage update: {
                        var before = await ReadRow(update.OldRow, update.Relation, cancellationToken).ConfigureAwait(false);
                        var after = await ReadRow(update.NewRow, update.Relation, cancellationToken).ConfigureAwait(false);
                        changes?.Add(new RowChange {
                            Table = TableOf(update.Relation), Kind = RowChangeKind.Update,
                            OldValues = before.Values, NewValues = after.Values, UnchangedColumns = after.Unchanged
                        });
                        break;
                    }

                    case IndexUpdateMessage update: {
                        // An update before REPLICA IDENTITY FULL took effect: no old row, so nothing can be ruled out.
                        await ReadRow(update.Key, update.Relation, cancellationToken).ConfigureAwait(false);
                        var after = await ReadRow(update.NewRow, update.Relation, cancellationToken).ConfigureAwait(false);
                        changes?.Add(new RowChange {
                            Table = TableOf(update.Relation), Kind = RowChangeKind.Update,
                            NewValues = after.Values, UnchangedColumns = after.Unchanged
                        });
                        break;
                    }

                    case UpdateMessage update: {
                        var after = await ReadRow(update.NewRow, update.Relation, cancellationToken).ConfigureAwait(false);
                        changes?.Add(new RowChange {
                            Table = TableOf(update.Relation), Kind = RowChangeKind.Update,
                            NewValues = after.Values, UnchangedColumns = after.Unchanged
                        });
                        break;
                    }

                    case FullDeleteMessage delete:
                        changes?.Add(new RowChange {
                            Table = TableOf(delete.Relation), Kind = RowChangeKind.Delete,
                            OldValues = (await ReadRow(delete.OldRow, delete.Relation, cancellationToken).ConfigureAwait(false)).Values,
                            NewValues = new Dictionary<string, object?>()
                        });
                        break;

                    case KeyDeleteMessage delete:
                        changes?.Add(new RowChange {
                            Table = TableOf(delete.Relation), Kind = RowChangeKind.Delete,
                            OldValues = (await ReadRow(delete.Key, delete.Relation, cancellationToken).ConfigureAwait(false)).Values,
                            NewValues = new Dictionary<string, object?>()
                        });
                        break;

                    case CommitMessage commit:
                        var position = new WritebackPosition((ulong)commit.TransactionEndLsn);
                        if (ours || changes is not { Count: > 0 }) {
                            // Nothing to send, so nothing to wait for: let the server release it straight away.
                            await ConfirmAsync(position, cancellationToken).ConfigureAwait(false);
                        } else {
                            yield return new ChangeTransaction { Changes = changes, Position = position };
                        }
                        changes = null;
                        break;
                }
            }
        }

        public async Task ConfirmAsync(WritebackPosition position, CancellationToken cancellationToken = default) {
            _connection.SetReplicationStatus(new NpgsqlLogSequenceNumber(position.Value));
            await _connection.SendStatusUpdate(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();

        private static WritebackTable TableOf(RelationMessage relation) => new(relation.Namespace, relation.RelationName);

        /// <summary>Reads a whole tuple: it has to be consumed before the next message, whether or not it is used.</summary>
        private static async Task<(Dictionary<string, object?> Values, HashSet<string> Unchanged)> ReadRow(
            ReplicationTuple tuple, RelationMessage relation, CancellationToken cancellationToken) {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var unchanged = new HashSet<string>(StringComparer.Ordinal);
            var i = 0;
            await foreach (var value in tuple.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var column = relation.Columns[i++].ColumnName;
                if (value.IsUnchangedToastedValue) {
                    unchanged.Add(column);
                    values[column] = null;
                } else if (value.IsDBNull) {
                    values[column] = null;
                } else {
                    values[column] = await value.Get(cancellationToken).ConfigureAwait(false);
                }
            }
            return (values, unchanged);
        }
    }

    #endregion

    #region Helpers

    private sealed record ServerFacts {
        public string WalLevel { get; init; } = "";
        public int MaxSlots { get; init; }
        public int UsedSlots { get; init; }
        public bool HasSlot { get; init; }
        public bool HasPublication { get; init; }
        public bool HasOrigin { get; init; }
        public string UserName { get; init; } = "";
        public bool IsSuperuser { get; init; }
        public bool CanTag { get; init; }
        public bool CanCreateOrigin { get; init; }
        public bool CanCreatePublication { get; init; }
    }

    private sealed record TableState {
        public string ReplicaIdentity { get; init; } = "";
        public bool CanAlter { get; init; }
        public bool InPublication { get; init; }
    }

    private static async Task<TableState?> TableFacts(NpgsqlConnection connection, WritebackTable table,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<TableState>(new CommandDefinition(@"
            SELECT c.relreplident::text AS ReplicaIdentity,
                   pg_has_role(c.relowner, 'USAGE') AS CanAlter,
                   EXISTS (SELECT 1 FROM pg_publication_tables p
                           WHERE p.pubname = @Publication AND p.schemaname = n.nspname AND p.tablename = c.relname) AS InPublication
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @Schema AND c.relname = @Table AND c.relkind IN ('r', 'p')",
            new { Publication = PublicationName, Schema = table.Schema ?? "public", table.Table },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static async Task<int> ServerVersion(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT current_setting('server_version_num')::int", cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken) {
        var connection = new NpgsqlConnection(connectionString);
        try {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        } catch {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task Execute(NpgsqlConnection connection, string sql, object? parameters, CancellationToken cancellationToken) {
        if (_debugQuery) {
            _logger.LogInformation("Writeback SQL: {Sql}", sql);
        }
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string QualifiedName(WritebackTable table) =>
        $"{Quote(table.Schema ?? "public")}.{Quote(table.Table)}";

    #endregion
}

/// <summary>
/// A Postgres repository whose every write carries this application's replication origin, so Writeback skips it.
/// </summary>
/// <remarks>
/// The origin is set up once per physical connection, when it is opened, and stays with it for as long as it is
/// pooled; this repository has a data source and therefore a pool of its own, so no untagged session ever
/// shares it.
/// </remarks>
public sealed class TaggedPostgresRepository : PostgresRepository {
    public TaggedPostgresRepository(ILogger<RepositoryBase> logger, string connectionString, bool debugQuery)
        : base(logger, connectionString, debugQuery, SetUpOrigin) { }

    private static async Task SetUpOrigin(NpgsqlConnection connection) {
        await using var command = new NpgsqlCommand("SELECT pg_replication_origin_session_setup(@Origin)", connection);
        command.Parameters.AddWithValue("Origin", PostgresWriteback.OriginName);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
