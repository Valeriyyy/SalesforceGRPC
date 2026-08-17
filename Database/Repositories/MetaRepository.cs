using Dapper;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Repositories;
public class MetaRepository : IMetaRepository {
    private readonly IMemoryCache _cache;
    private readonly ILogger<MetaRepository> _logger;
    private readonly string _connectionString;
    private readonly bool _debugQuery = false;
    private const string MappingCacheKeyPrefix = "mapping_";
    private const string SchemaCacheKeyPrefix = "schemas";

    /// <summary>
    /// The columns of a Binding, aliased to its properties. Kept in one place because they are selected from
    /// four queries and an alias that does not match a property leaves it silently null — which is how
    /// soft_delete_enabled came to be always false and schema_name always null.
    /// </summary>
    private const string BindingColumns = @"
                cs.id as Id,
                cs.avro_schema_id as AvroSchemaId,
                cs.entity_name as EntityName,
                cs.db_schema_full_name as DbSchemaFullName,
                cs.binding_state as BindingState,
                cs.soft_delete_enabled as SoftDeleteEnabled,
                cs.soft_delete_column_name as SoftDeleteColumnName,
                avro.id as Id,
                avro.schema_id as SchemaId,
                avro.record_name as RecordName,
                avro.schema_json as SchemaJson,
                avro.date_created as DateCreated,
                avro.date_updated as DateUpdated";

    private const string BindingFrom = @"
            FROM salesforce.cdc_schemas cs
            LEFT JOIN salesforce.avro_schemas avro ON cs.avro_schema_id = avro.id";

    public MetaRepository(IMemoryCache cache, IConfiguration configuration, ILogger<MetaRepository> logger) {
        _cache = cache;
        _logger = logger;
        if(configuration.GetConnectionString("appDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("appDatabase")!;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
    }

    public async Task<Dictionary<string, string>> GetCachedMapping(int? schemaId, CancellationToken cancellationToken) {
        if (schemaId is null) {
            return new Dictionary<string, string>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        string cacheKey = $"{MappingCacheKeyPrefix}{schemaId}";

        // Try to get from cache
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? cachedMapping)) {
            return cachedMapping!;
        }

        // Fetch mappings if not in cache
        var mappings = await GetEntityMappedFieldsBySchemaId(schemaId).ConfigureAwait(false);

        // Build the mapping dictionary from the provided mappings
        var mapping = mappings
            .Where(m => m.SchemaId == schemaId)
            .ToDictionary(m => m.SalesforceFieldName, m => m.TargetFieldName);

        // Store in cache with a 1-hour sliding expiration
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromHours(1));

        _cache.Set(cacheKey, mapping, cacheOptions);

        return mapping;
    }

    public async Task<IEnumerable<MappedField>> GetEntityMappedFieldsBySchemaId(int? schemaId) {
        await using var connection = new NpgsqlConnection(_connectionString);

        return await connection.QueryAsync<MappedField>(
            @"SELECT id as ID,
            schema_id as SchemaId,
            salesforce_field_name as SalesforceFieldName,
            target_field_name as TargetFieldName
            FROM salesforce.mapped_fields WHERE schema_id = @SchemaId",
            new { SchemaId = schemaId }).ConfigureAwait(false);
    }

    public async Task<CDCSchema?> GetSchemaById(int schemaId) {
        return await QuerySingleBinding($"SELECT {BindingColumns} {BindingFrom} WHERE cs.id = @Value", schemaId)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a Binding by the Avro schema's record name.
    /// </summary>
    public async Task<CDCSchema?> GetSchemaByRecordName(string recordName) {
        return await QuerySingleBinding(
            $"SELECT {BindingColumns} {BindingFrom} WHERE avro.record_name = @Value", recordName)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the Binding for an Entity. Entity name is unique across Bindings.
    /// </summary>
    public async Task<CDCSchema?> GetSchemaByEntityName(string entityName) {
        return await QuerySingleBinding(
            $"SELECT {BindingColumns} {BindingFrom} WHERE cs.entity_name = @Value", entityName)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the Binding writing to a Target Table. Target Table is unique across Bindings.
    /// </summary>
    public async Task<CDCSchema?> GetSchemaByTargetTable(string dbSchemaFullName) {
        return await QuerySingleBinding(
            $"SELECT {BindingColumns} {BindingFrom} WHERE cs.db_schema_full_name = @Value", dbSchemaFullName)
            .ConfigureAwait(false);
    }

    private async Task<CDCSchema?> QuerySingleBinding(string sql, object value) {
        await using var connection = new NpgsqlConnection(_connectionString);

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Value: {Value}", "SELECT", sql, value);
        }

        var res = await connection.QueryAsync<CDCSchema, DbAvroSchema, CDCSchema>(
            sql,
            (cdcSchema, avroSchema) => {
                cdcSchema.AvroSchema = avroSchema;
                return cdcSchema;
            },
            new { Value = value },
            splitOn: "Id"
        ).ConfigureAwait(false);

        return res.FirstOrDefault();
    }

    public async Task<List<CDCSchema>> GetCachedSchemas(CancellationToken cancellationToken = default) {
        // Try to get from cache
        if (_cache.TryGetValue(SchemaCacheKeyPrefix, out List<CDCSchema>? cachedSchemas)) {
            return cachedSchemas!;
        }

        var schemas = (await GetAllSchemas(cancellationToken)).ToList();

        // Store in cache with a 1-hour sliding expiration
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromHours(1));
        _cache.Set(SchemaCacheKeyPrefix, schemas, cacheOptions);

        return schemas.ToList();
    }

    public async Task<CDCSchema> CreateNewSchema(CDCSchema dbSchema) {
        _cache.Remove(SchemaCacheKeyPrefix);

        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            INSERT INTO salesforce.cdc_schemas (entity_name, db_schema_full_name, binding_state, soft_delete_enabled, soft_delete_column_name)
            VALUES (@EntityName, @DbSchemaFullName, @BindingState, @SoftDeleteEnabled, @SoftDeleteColumnName)
            RETURNING id as Id, entity_name as EntityName, db_schema_full_name as DbSchemaFullName, binding_state as BindingState, soft_delete_enabled as SoftDeleteEnabled, soft_delete_column_name as SoftDeleteColumnName;";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}", "INSERT", sql);
        }

        var insertedRecord = await connection.QuerySingleAsync<CDCSchema>(
            sql,
            new {
                dbSchema.EntityName,
                dbSchema.DbSchemaFullName,
                BindingState = dbSchema.BindingState.ToString(),
                dbSchema.SoftDeleteEnabled,
                dbSchema.SoftDeleteColumnName
            }).ConfigureAwait(false);

        return insertedRecord;
    }

    /// <summary>
    /// Creates a new Binding and links it to an Avro schema.
    /// </summary>
    public async Task<CDCSchema> CreateNewSchemaWithAvroLink(CDCSchema dbSchema, int avroSchemaId) {
        _cache.Remove(SchemaCacheKeyPrefix);

        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            INSERT INTO salesforce.cdc_schemas (entity_name, db_schema_full_name, binding_state, soft_delete_enabled, soft_delete_column_name, avro_schema_id)
            VALUES (@EntityName, @DbSchemaFullName, @BindingState, @SoftDeleteEnabled, @SoftDeleteColumnName, @AvroSchemaId)
            RETURNING id as Id, avro_schema_id as AvroSchemaId, entity_name as EntityName, db_schema_full_name as DbSchemaFullName, binding_state as BindingState, soft_delete_enabled as SoftDeleteEnabled, soft_delete_column_name as SoftDeleteColumnName;";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, AvroSchemaId: {AvroSchemaId}", "INSERT", sql, avroSchemaId);
        }

        var insertedRecord = await connection.QuerySingleAsync<CDCSchema>(
            sql,
            new {
                dbSchema.EntityName,
                dbSchema.DbSchemaFullName,
                BindingState = dbSchema.BindingState.ToString(),
                dbSchema.SoftDeleteEnabled,
                dbSchema.SoftDeleteColumnName,
                AvroSchemaId = avroSchemaId
            }).ConfigureAwait(false);

        if (dbSchema.AvroSchema != null) {
            insertedRecord.AvroSchema = dbSchema.AvroSchema;
        }

        return insertedRecord;
    }

    /// <summary>
    /// Updates an existing Binding with a new Avro schema link.
    /// </summary>
    public async Task<bool> UpdateCdcSchemaWithAvroLink(int cdcSchemaId, int avroSchemaId) {
        _cache.Remove(SchemaCacheKeyPrefix);

        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            UPDATE salesforce.cdc_schemas
            SET avro_schema_id = @AvroSchemaId
            WHERE id = @CdcSchemaId";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, CdcSchemaId: {CdcSchemaId}, AvroSchemaId: {AvroSchemaId}",
                "UPDATE", sql, cdcSchemaId, avroSchemaId);
        }

        var affectedRows = await connection.ExecuteAsync(sql,
            new { CdcSchemaId = cdcSchemaId, AvroSchemaId = avroSchemaId }).ConfigureAwait(false);

        return affectedRows > 0;
    }

    public async Task<bool> UpdateBinding(int bindingId, string dbSchemaFullName, bool softDeleteEnabled,
        string? softDeleteColumnName) {
        InvalidateBinding(bindingId);

        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            UPDATE salesforce.cdc_schemas
            SET db_schema_full_name = @DbSchemaFullName,
                soft_delete_enabled = @SoftDeleteEnabled,
                soft_delete_column_name = @SoftDeleteColumnName
            WHERE id = @BindingId";

        var affectedRows = await connection.ExecuteAsync(sql, new {
            BindingId = bindingId,
            DbSchemaFullName = dbSchemaFullName,
            SoftDeleteEnabled = softDeleteEnabled,
            SoftDeleteColumnName = softDeleteColumnName
        }).ConfigureAwait(false);

        return affectedRows > 0;
    }

    public async Task<bool> SetBindingState(int bindingId, BindingState state) {
        InvalidateBinding(bindingId);

        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            UPDATE salesforce.cdc_schemas
            SET binding_state = @State
            WHERE id = @BindingId";

        var affectedRows = await connection.ExecuteAsync(sql,
            new { BindingId = bindingId, State = state.ToString() }).ConfigureAwait(false);

        return affectedRows > 0;
    }

    public async Task<bool> DeleteBinding(int bindingId) {
        InvalidateBinding(bindingId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        // mapped_fields has no cascade to cdc_schemas, so the Field Mappings go first or they are orphaned.
        await connection.ExecuteAsync(
            "DELETE FROM salesforce.mapped_fields WHERE schema_id = @BindingId",
            new { BindingId = bindingId }, transaction).ConfigureAwait(false);

        var affectedRows = await connection.ExecuteAsync(
            "DELETE FROM salesforce.cdc_schemas WHERE id = @BindingId",
            new { BindingId = bindingId }, transaction).ConfigureAwait(false);

        await transaction.CommitAsync().ConfigureAwait(false);

        return affectedRows > 0;
    }

    public async Task ReplaceFieldMappings(int bindingId, IEnumerable<MappedField> mappings) {
        InvalidateBinding(bindingId);

        var rows = mappings.ToList();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        await connection.ExecuteAsync(
            "DELETE FROM salesforce.mapped_fields WHERE schema_id = @BindingId",
            new { BindingId = bindingId }, transaction).ConfigureAwait(false);

        if (rows.Count > 0) {
            await connection.ExecuteAsync(
                @"INSERT INTO salesforce.mapped_fields (schema_id, salesforce_field_name, target_field_name)
                  VALUES (@SchemaId, @SalesforceFieldName, @TargetFieldName)",
                rows.Select(m => new {
                    SchemaId = bindingId,
                    m.SalesforceFieldName,
                    m.TargetFieldName
                }), transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);

        if (_debugQuery) {
            _logger.LogInformation("Replaced {Count} field mappings for binding {BindingId}", rows.Count, bindingId);
        }
    }

    /// <summary>
    /// Drops both caches a Binding write can invalidate.
    /// </summary>
    /// <remarks>
    /// The mapping cache is per-Binding and previously had no invalidation at all, so a mapping edited through
    /// the API took up to an hour to reach the running worker.
    /// </remarks>
    private void InvalidateBinding(int bindingId) {
        _cache.Remove(SchemaCacheKeyPrefix);
        _cache.Remove($"{MappingCacheKeyPrefix}{bindingId}");
    }

    public async Task<IEnumerable<CDCSchema>> GetAllSchemas(CancellationToken cancellationToken) {
        await using var connection = new NpgsqlConnection(_connectionString);

        var sql = $"SELECT {BindingColumns} {BindingFrom} ORDER BY cs.entity_name";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}", "SELECT", sql);
        }

        var res = await connection.QueryAsync<CDCSchema, DbAvroSchema, CDCSchema>(
            sql,
            (cdcSchema, avroSchema) => {
                cdcSchema.AvroSchema = avroSchema;
                return cdcSchema;
            },
            splitOn: "Id"
        ).ConfigureAwait(false);

        return res;
    }
}
