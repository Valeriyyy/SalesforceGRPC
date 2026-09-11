using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace Database.Repositories;

/// <summary>
/// Dapper-backed store for the single Target Connection. Always talks to the App Database
/// (ConnectionStrings:appDatabase), like the other metadata repositories — this row describes the target
/// database; it does not live in it.
/// </summary>
public class TargetConnectionRepository : ITargetConnectionRepository {
    private readonly ILogger<TargetConnectionRepository> _logger;
    private readonly string _connectionString;
    private readonly bool _debugQuery;

    /// <summary>
    /// The columns of a Target Connection, aliased to the row type. Kept in one place because an alias that
    /// does not match a property leaves it silently null, which for a credential means an opaque connection
    /// failure rather than a visible mistake.
    /// </summary>
    private const string ConnectionColumns = @"
                id AS Id,
                engine AS Engine,
                host AS Host,
                port AS Port,
                database_name AS DatabaseName,
                username AS Username,
                password_encrypted AS PasswordEncrypted,
                file_path AS FilePath,
                options::text AS OptionsJson,
                connection_state AS ConnectionState,
                last_connected_at AS LastConnectedAt,
                last_error AS LastError,
                last_error_raw AS LastErrorRaw,
                last_error_at AS LastErrorAt,
                date_created AS DateCreated,
                date_updated AS DateUpdated";

    /// <summary>
    /// What Dapper materialises. Differs from the model only in carrying the options document as text, since
    /// jsonb has no natural CLR mapping and a global type handler for a dictionary would reach every query in
    /// the process.
    /// </summary>
    private sealed class Row {
        public int Id { get; set; }
        public TargetDatabaseEngine Engine { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? DatabaseName { get; set; }
        public string? Username { get; set; }
        public string? PasswordEncrypted { get; set; }
        public string? FilePath { get; set; }
        public string OptionsJson { get; set; } = "{}";
        public ConnectionState ConnectionState { get; set; }
        public DateTime? LastConnectedAt { get; set; }
        public string? LastError { get; set; }
        public string? LastErrorRaw { get; set; }
        public DateTime? LastErrorAt { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public TargetConnection ToModel() => new() {
            Id = Id,
            Engine = Engine,
            Host = Host,
            Port = Port,
            DatabaseName = DatabaseName,
            Username = Username,
            PasswordEncrypted = PasswordEncrypted,
            FilePath = FilePath,
            Options = JsonSerializer.Deserialize<Dictionary<string, string>>(OptionsJson) ?? new(StringComparer.Ordinal),
            ConnectionState = ConnectionState,
            LastConnectedAt = LastConnectedAt,
            LastError = LastError,
            LastErrorRaw = LastErrorRaw,
            LastErrorAt = LastErrorAt,
            DateCreated = DateCreated,
            DateUpdated = DateUpdated
        };
    }

    public TargetConnectionRepository(ILogger<TargetConnectionRepository> logger, IConfiguration configuration) {
        _logger = logger;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
        _connectionString = configuration.GetConnectionString("appDatabase")
            ?? throw new InvalidOperationException("Db connection string is not configured.");
    }

    public async Task<TargetConnection?> GetAsync(CancellationToken cancellationToken = default) {
        var sql = $"SELECT {ConnectionColumns} FROM salesforce.target_connection LIMIT 1";
        LogQuery("SELECT", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row?.ToModel();
    }

    // ON CONFLICT on is_singleton rather than on id: the caller creating a connection has no id to supply,
    // and the unique constraint is what makes "the one row" a fact rather than an assumption.
    private const string UpsertSql = $@"
            INSERT INTO salesforce.target_connection (
                is_singleton, engine, host, port, database_name, username, password_encrypted, file_path,
                options, connection_state)
            VALUES (
                true, @Engine, @Host, @Port, @DatabaseName, @Username, @PasswordEncrypted, @FilePath,
                @OptionsJson::jsonb, 'Incomplete')
            ON CONFLICT (is_singleton) DO UPDATE SET
                engine = EXCLUDED.engine,
                host = EXCLUDED.host,
                port = EXCLUDED.port,
                database_name = EXCLUDED.database_name,
                username = EXCLUDED.username,
                password_encrypted = EXCLUDED.password_encrypted,
                file_path = EXCLUDED.file_path,
                options = EXCLUDED.options,
                -- Changed details say nothing about whether they work, so the connection goes back to
                -- Incomplete and the previous outcome is cleared rather than left to look current.
                connection_state = 'Incomplete',
                last_connected_at = NULL,
                last_error = NULL,
                last_error_raw = NULL,
                last_error_at = NULL,
                date_updated = now()
            RETURNING {ConnectionColumns}";

    private static object UpsertParameters(TargetConnection targetConnection) => new {
        Engine = targetConnection.Engine.ToString(),
        targetConnection.Host,
        targetConnection.Port,
        targetConnection.DatabaseName,
        targetConnection.Username,
        targetConnection.PasswordEncrypted,
        targetConnection.FilePath,
        OptionsJson = JsonSerializer.Serialize(targetConnection.Options)
    };

    /// <inheritdoc />
    public async Task<TargetConnection> UpsertAsync(TargetConnection targetConnection, CancellationToken cancellationToken = default) {
        LogQuery("UPSERT", UpsertSql);

        await using var connection = new NpgsqlConnection(_connectionString);
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(UpsertSql,
            UpsertParameters(targetConnection), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row.ToModel();
    }

    private const string RecordSuccessSql = @"
            UPDATE salesforce.target_connection SET
                connection_state = 'Connected',
                last_connected_at = @At,
                last_error = NULL,
                last_error_raw = NULL,
                last_error_at = NULL,
                date_updated = now()";

    public async Task RecordSuccessAsync(DateTime at, CancellationToken cancellationToken = default) {
        LogQuery("UPDATE", RecordSuccessSql);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(RecordSuccessSql, new { At = at }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public Task RecordFailureAsync(string error, string? rawResponse, DateTime at, CancellationToken cancellationToken = default) =>
        // last_connected_at survives a failure on purpose: "worked until 04:12, then this" is the useful
        // report, and clearing it would erase the only evidence the connection ever worked.
        RecordOutcomeAsync(nameof(ConnectionState.Failed), error, rawResponse, at, cancellationToken);

    public Task RecordIncompleteAsync(string error, string? rawResponse, DateTime at, CancellationToken cancellationToken = default) =>
        RecordOutcomeAsync(nameof(ConnectionState.Incomplete), error, rawResponse, at, cancellationToken);

    private async Task RecordOutcomeAsync(string state, string error, string? rawResponse, DateTime at,
        CancellationToken cancellationToken) {
        const string sql = @"
            UPDATE salesforce.target_connection SET
                connection_state = @State,
                last_error = @Error,
                last_error_raw = @RawResponse,
                last_error_at = @At,
                date_updated = now()";

        LogQuery("UPDATE", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { State = state, Error = error, RawResponse = rawResponse, At = at },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string CountBindingsSql = @"
            SELECT
                (SELECT count(*) FROM salesforce.cdc_schemas) AS Bindings,
                (SELECT count(*) FROM salesforce.mapped_fields) AS FieldMappings";

    public async Task<BindingCounts> CountBindingsAsync(CancellationToken cancellationToken = default) {
        LogQuery("SELECT", CountBindingsSql);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<BindingCounts>(
            new CommandDefinition(CountBindingsSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<(TargetConnection Connection, BindingCounts Destroyed)> RepointAsync(
        TargetConnection targetConnection, DateTime provedAt, CancellationToken cancellationToken = default) {
        // Deleted in dependency order rather than leaning on ON DELETE CASCADE, so the statements read as the
        // list of what a repoint destroys. Channel Members are not touched: their cdc_schema_id is
        // ON DELETE SET NULL, which is exactly the outcome wanted.
        const string destroy = @"
            DELETE FROM salesforce.mapped_fields;
            DELETE FROM salesforce.cdc_schemas;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var counts = await connection.QuerySingleAsync<BindingCounts>(new CommandDefinition(CountBindingsSql,
            transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        LogQuery("DELETE", destroy);
        await connection.ExecuteAsync(new CommandDefinition(destroy, transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        LogQuery("UPSERT", UpsertSql);
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(UpsertSql,
            UpsertParameters(targetConnection), transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        // Proved before this was called, so it is Connected from the moment it exists.
        await connection.ExecuteAsync(new CommandDefinition(RecordSuccessSql, new { At = provedAt },
            transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        row.ConnectionState = ConnectionState.Connected;
        row.LastConnectedAt = provedAt;

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Repointed the Target Connection to {Engine} {Address}. Destroyed {Bindings} Binding(s) and {Mappings} Field Mapping(s).",
            row.Engine, row.Host ?? row.FilePath, counts.Bindings, counts.FieldMappings);

        return (row.ToModel(), counts);
    }

    private void LogQuery(string operation, string sql) {
        if (_debugQuery) {
            _logger.LogDebug("TargetConnection {Operation}: {Sql}", operation, sql);
        }
    }
}
