using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Repositories;

/// <summary>
/// Dapper-backed store for the single Org Connection. Always talks to the app database
/// (ConnectionStrings:appDatabase), like the other metadata repositories — only the <em>target</em> database
/// is pluggable.
/// </summary>
public class OrgConnectionRepository : IOrgConnectionRepository {
    private readonly ILogger<OrgConnectionRepository> _logger;
    private readonly string _connectionString;
    private readonly bool _debugQuery;

    /// <summary>
    /// The columns of an Org Connection, aliased to its properties. Kept in one place because an alias that
    /// does not match a property leaves it silently null, which for a credential means an opaque
    /// authentication failure rather than a visible mistake.
    /// </summary>
    private const string ConnectionColumns = @"
                id AS Id,
                consumer_key AS ConsumerKey,
                administering_username AS AdministeringUsername,
                run_as_username AS RunAsUsername,
                is_sandbox AS IsSandbox,
                signing_private_key AS SigningPrivateKey,
                signing_certificate AS SigningCertificate,
                certificate_fingerprint AS CertificateFingerprint,
                certificate_expires_at AS CertificateExpiresAt,
                org_url AS OrgUrl,
                org_id AS OrgId,
                connection_state AS ConnectionState,
                last_connected_at AS LastConnectedAt,
                last_error AS LastError,
                last_error_at AS LastErrorAt,
                bootstrap_consumer_secret AS BootstrapConsumerSecret,
                bootstrap_refresh_token AS BootstrapRefreshToken,
                date_created AS DateCreated,
                date_updated AS DateUpdated";

    public OrgConnectionRepository(ILogger<OrgConnectionRepository> logger, IConfiguration configuration) {
        _logger = logger;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
        if (configuration.GetConnectionString("appDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("appDatabase")!;
    }

    public async Task<OrgConnection?> GetAsync(CancellationToken cancellationToken = default) {
        var sql = $"SELECT {ConnectionColumns} FROM salesforce.org_connection LIMIT 1";
        LogQuery("SELECT", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<OrgConnection>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OrgConnection> UpsertAsync(OrgConnection orgConnection, CancellationToken cancellationToken = default) {
        // ON CONFLICT on is_singleton rather than on id: the caller creating a connection has no id to
        // supply, and the unique constraint is what makes "the one row" a fact rather than an assumption.
        var sql = $@"
            INSERT INTO salesforce.org_connection (
                is_singleton, consumer_key, administering_username, run_as_username, is_sandbox,
                signing_private_key, signing_certificate, certificate_fingerprint, certificate_expires_at,
                connection_state)
            VALUES (
                true, @ConsumerKey, @AdministeringUsername, @RunAsUsername, @IsSandbox,
                @SigningPrivateKey, @SigningCertificate, @CertificateFingerprint, @CertificateExpiresAt,
                @ConnectionState)
            ON CONFLICT (is_singleton) DO UPDATE SET
                consumer_key = EXCLUDED.consumer_key,
                administering_username = EXCLUDED.administering_username,
                run_as_username = EXCLUDED.run_as_username,
                is_sandbox = EXCLUDED.is_sandbox,
                signing_private_key = EXCLUDED.signing_private_key,
                signing_certificate = EXCLUDED.signing_certificate,
                certificate_fingerprint = EXCLUDED.certificate_fingerprint,
                certificate_expires_at = EXCLUDED.certificate_expires_at,
                -- Changed details say nothing about whether they work, so the connection goes back to
                -- Incomplete and the previous success is cleared rather than left to look current.
                connection_state = 'Incomplete',
                org_url = NULL,
                org_id = NULL,
                last_connected_at = NULL,
                last_error = NULL,
                last_error_at = NULL,
                date_updated = now()
            RETURNING {ConnectionColumns}";

        LogQuery("UPSERT", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<OrgConnection>(new CommandDefinition(sql, new {
            orgConnection.ConsumerKey,
            orgConnection.AdministeringUsername,
            orgConnection.RunAsUsername,
            orgConnection.IsSandbox,
            orgConnection.SigningPrivateKey,
            orgConnection.SigningCertificate,
            orgConnection.CertificateFingerprint,
            orgConnection.CertificateExpiresAt,
            ConnectionState = nameof(ConnectionState.Incomplete)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task RecordSuccessAsync(string orgUrl, string orgId, DateTime at, CancellationToken cancellationToken = default) {
        const string sql = @"
            UPDATE salesforce.org_connection SET
                connection_state = 'Connected',
                org_url = @OrgUrl,
                org_id = @OrgId,
                last_connected_at = @At,
                last_error = NULL,
                last_error_at = NULL,
                date_updated = now()";

        LogQuery("UPDATE", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { OrgUrl = orgUrl, OrgId = orgId, At = at }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(string error, DateTime at, CancellationToken cancellationToken = default) {
        // last_connected_at survives a failure on purpose: "worked until 04:12, then this" is the useful
        // report, and clearing it would erase the only evidence the connection ever worked.
        const string sql = @"
            UPDATE salesforce.org_connection SET
                connection_state = 'Failed',
                last_error = @Error,
                last_error_at = @At,
                date_updated = now()";

        LogQuery("UPDATE", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { Error = error, At = at }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SaveBootstrapSecretsAsync(string? encryptedConsumerSecret, string? encryptedRefreshToken,
        CancellationToken cancellationToken = default) {
        const string sql = @"
            UPDATE salesforce.org_connection SET
                bootstrap_consumer_secret = @ConsumerSecret,
                bootstrap_refresh_token = @RefreshToken,
                date_updated = now()";

        LogQuery("UPDATE", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { ConsumerSecret = encryptedConsumerSecret, RefreshToken = encryptedRefreshToken },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task PurgeBootstrapSecretsAsync(CancellationToken cancellationToken = default) {
        const string sql = @"
            UPDATE salesforce.org_connection SET
                bootstrap_consumer_secret = NULL,
                bootstrap_refresh_token = NULL,
                date_updated = now()
            WHERE bootstrap_consumer_secret IS NOT NULL OR bootstrap_refresh_token IS NOT NULL";

        LogQuery("UPDATE", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        var purged = await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (purged > 0) {
            _logger.LogInformation("Purged the Bootstrap session material; the connection now authenticates by JWT alone");
        }
    }

    public async Task<OrgScopedStateCounts> CountOrgScopedStateAsync(CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT
                (SELECT count(*) FROM salesforce.cdc_schemas) AS Bindings,
                (SELECT count(*) FROM salesforce.mapped_fields) AS FieldMappings,
                (SELECT count(*) FROM salesforce.avro_schemas) AS AvroSchemas,
                (SELECT count(*) FROM salesforce.platform_event_channels) AS Channels,
                (SELECT count(*) FROM salesforce.platform_event_channel_members) AS ChannelMembers";

        LogQuery("SELECT", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<OrgScopedStateCounts>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<OrgScopedStateCounts> DeleteConnectionAndOrgScopedStateAsync(CancellationToken cancellationToken = default) {
        var counts = await CountOrgScopedStateAsync(cancellationToken).ConfigureAwait(false);

        // Deleted in dependency order rather than leaning on ON DELETE CASCADE, so the statements read as the
        // list of what Disconnect destroys and stay honest if a cascade rule is ever relaxed.
        const string sql = @"
            DELETE FROM salesforce.platform_event_channel_members;
            DELETE FROM salesforce.platform_event_channels;
            DELETE FROM salesforce.mapped_fields;
            DELETE FROM salesforce.cdc_schemas;
            DELETE FROM salesforce.avro_schemas;
            DELETE FROM salesforce.org_connection;";

        LogQuery("DELETE", sql);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Disconnected. Destroyed {Bindings} Binding(s), {Mappings} Field Mapping(s), {Schemas} Avro Schema(s) " +
            "and {Channels} mirrored Channel(s). Nothing was changed inside Salesforce.",
            counts.Bindings, counts.FieldMappings, counts.AvroSchemas, counts.Channels);

        return counts;
    }

    private void LogQuery(string operation, string sql) {
        if (_debugQuery) {
            _logger.LogDebug("OrgConnection {Operation}: {Sql}", operation, sql);
        }
    }
}
