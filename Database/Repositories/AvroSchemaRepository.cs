using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Repositories;

public class AvroSchemaRepository : IAvroSchemaRepository {
    private readonly ILogger<AvroSchemaRepository> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;
    private readonly bool _debugQuery = false;

    public AvroSchemaRepository(ILogger<AvroSchemaRepository> logger, IConfiguration configuration) {
        _logger = logger;
        _configuration = configuration;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
        if (configuration.GetConnectionString("appDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("appDatabase")!;
    }

    #region Create
    /// <summary>
    /// Inserts a new Avro schema into the database.
    /// </summary>
    public async Task<int> InsertSchemaAsync(DbAvroSchema avroSchema, CancellationToken cancellationToken = default) {
        if (avroSchema?.RecordName is null || avroSchema.SchemaJson is null || avroSchema.SchemaId is null) {
            throw new ArgumentNullException(nameof(avroSchema));
        }

        const string sql = @"
            INSERT INTO salesforce.avro_schemas 
            (schema_id, record_name, schema_json)
            VALUES 
            (@SchemaId, @RecordName, @SchemaJson::jsonb)
            RETURNING id";

        var parameters = new {
            SchemaId = avroSchema.SchemaId,
            RecordName = avroSchema.RecordName,
            SchemaJson = avroSchema.SchemaJson
        };

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "INSERT", sql, parameters);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var id = await connection.QuerySingleAsync<int>(sql, parameters).ConfigureAwait(false);
        return id;
    }
    #endregion

    #region Read
    /// <summary>
    /// Retrieves an Avro schema by its ID.
    /// </summary>
    public async Task<DbAvroSchema?> GetSchemaByIdAsync(int id, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT 
                id AS Id,
                schema_id AS SchemaId,
                record_name AS RecordName,
                schema_json AS SchemaJson,
                date_created AS DateCreated,
                date_updated AS DateUpdated
            FROM salesforce.avro_schemas
            WHERE id = @Id";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Id: {Id}", "SELECT", sql, id);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var schema = await connection.QuerySingleOrDefaultAsync<DbAvroSchema>(sql, new { Id = id }).ConfigureAwait(false);
        return schema;
    }

    /// <summary>
    /// Retrieves an Avro schema by schema_id.
    /// </summary>
    public async Task<DbAvroSchema?> GetSchemaBySchemaIdAsync(string schemaId, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT 
                id AS Id,
                schema_id AS SchemaId,
                record_name AS RecordName,
                schema_json AS SchemaJson,
                date_created AS DateCreated,
                date_updated AS DateUpdated
            FROM salesforce.avro_schemas
            WHERE schema_id = @SchemaId";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, SchemaId: {SchemaId}", "SELECT", sql, schemaId);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var schema = await connection.QuerySingleOrDefaultAsync<DbAvroSchema>(sql, new { SchemaId = schemaId }).ConfigureAwait(false);
        return schema;
    }

    /// <summary>
    /// Retrieves Avro schemas by record name.
    /// </summary>
    public async Task<List<DbAvroSchema>> GetSchemaByRecordNameAsync(string recordName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT 
                id AS Id,
                schema_id AS SchemaId,
                record_name AS RecordName,
                schema_json AS SchemaJson,
                date_created AS DateCreated,
                date_updated AS DateUpdated
            FROM salesforce.avro_schemas
            WHERE record_name = @RecordName
            ORDER BY date_created DESC";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordName: {RecordName}", "SELECT", sql, recordName);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var schemas = (await connection.QueryAsync<DbAvroSchema>(sql, new { RecordName = recordName }).ConfigureAwait(false)).ToList();
        return schemas;
    }

    /// <summary>
    /// Retrieves all active Avro schemas.
    /// </summary>
    public async Task<List<DbAvroSchema>> GetAllActiveSchemaAsync(CancellationToken cancellationToken = default) {
        /*const string sql = @"
            SELECT 
                id AS Id,
                schema_id AS SchemaId,
                record_name AS RecordName,
                schema_json AS SchemaJson,
                date_created AS DateCreated,
                date_updated AS DateUpdated
            FROM salesforce.avro_schemas
            WHERE is_active = true
            ORDER BY record_name, date_created DESC";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}", "SELECT", sql);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var schemas = (await connection.QueryAsync<DbAvroSchema>(sql).ConfigureAwait(false)).ToList();
        return schemas;*/
        return [];
    }

    /// <summary>
    /// Retrieves all Avro schemas, active and inactive.
    /// </summary>
    public async Task<List<DbAvroSchema>> GetAllSchemasAsync(CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT 
                id AS Id,
                schema_id AS SchemaId,
                record_name AS RecordName,
                schema_json AS SchemaJson,
                date_created AS DateCreated,
                date_updated AS DateUpdated
            FROM salesforce.avro_schemas
            ORDER BY date_created DESC";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}", "SELECT", sql);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var schemas = (await connection.QueryAsync<DbAvroSchema>(sql).ConfigureAwait(false)).ToList();
        return schemas;
    }
    #endregion

    #region Update
    /// <summary>
    /// Updates an existing Avro schema.
    /// </summary>
    public async Task<bool> UpdateSchemaAsync(DbAvroSchema avroSchema, CancellationToken cancellationToken = default) {
        if (avroSchema?.RecordName is null || avroSchema.SchemaJson is null) {
            throw new ArgumentNullException(nameof(avroSchema));
        }

        const string sql = @"
            UPDATE salesforce.avro_schemas
            SET 
                record_name = @RecordName,
                schema_json = @SchemaJson::jsonb,
                date_updated = @DateUpdated
            WHERE id = @Id";

        var parameters = new {
            Id = avroSchema.Id,
            RecordName = avroSchema.RecordName,
            SchemaJson = avroSchema.SchemaJson,
            DateUpdated = DateTime.UtcNow
        };

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "UPDATE", sql, parameters);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
        return affectedRows > 0;
    }

    /// <summary>
    /// Sets a schema as active or inactive.
    /// </summary>
    public async Task<bool> SetActiveStatusAsync(int schemaId, bool isActive, CancellationToken cancellationToken = default) {
        /*const string sql = @"
            UPDATE salesforce.avro_schemas
            SET is_active = @IsActive, date_updated = @DateUpdated
            WHERE id = @Id";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Id: {Id}, IsActive: {IsActive}", "UPDATE", sql, schemaId, isActive);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(sql, 
            new { Id = schemaId, IsActive = isActive, DateUpdated = DateTime.UtcNow }).ConfigureAwait(false);
        return affectedRows > 0;*/
        await Task.Delay(0, cancellationToken); // Simulate async operation
        return false;
    }
    #endregion

    #region Delete
    /// <summary>
    /// Deletes an Avro schema from the database.
    /// </summary>
    public async Task<bool> DeleteSchemaAsync(int id, CancellationToken cancellationToken = default) {
        const string sql = "DELETE FROM salesforce.avro_schemas WHERE id = @Id";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Id: {Id}", "DELETE", sql, id);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(sql, new { Id = id }).ConfigureAwait(false);
        return affectedRows > 0;
    }

    /// <summary>
    /// Deletes Avro schemas by record name.
    /// </summary>
    public async Task<int> DeleteSchemasByRecordNameAsync(string recordName, CancellationToken cancellationToken = default) {
        const string sql = "DELETE FROM salesforce.avro_schemas WHERE record_name = @RecordName";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordName: {RecordName}", "DELETE", sql, recordName);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(sql, new { RecordName = recordName }).ConfigureAwait(false);
        return affectedRows;
    }
    #endregion
}