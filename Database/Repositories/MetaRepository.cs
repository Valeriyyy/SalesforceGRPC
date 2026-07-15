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
        await using var connection = new NpgsqlConnection(_connectionString);
        
        const string sql = @"
            SELECT 
                cs.id as Id, 
                cs.avro_schema_id as AvroSchemaId,
                cs.entity_name as EntityName, 
                cs.db_schema_full_name as DbSchemaFullName,
                cs.soft_delete_enabled as SoftDeleteEnabled,
                cs.soft_delete_column_name as SoftDeleteColumnName,
                avro.id as Id,
                avro.schema_id as SchemaId,
                avro.record_name as RecordName,
                avro.schema_json as SchemaJson,
                avro.date_created as DateCreated,
                avro.date_updated as DateUpdated
            FROM salesforce.cdc_schemas cs
            LEFT JOIN salesforce.avro_schemas avro ON cs.avro_schema_id = avro.id
            WHERE cs.id = @SchemaId";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, SchemaId: {SchemaId}", "SELECT", sql, schemaId);
        }

        var res = await connection.QueryAsync<CDCSchema, DbAvroSchema, CDCSchema>(
            sql,
            (cdcSchema, avroSchema) => {
                cdcSchema.AvroSchema = avroSchema;
                return cdcSchema;
            },
            new { SchemaId = schemaId },
            splitOn: "Id"
        ).ConfigureAwait(false);

        return res.FirstOrDefault();
    }

    /// <summary>
    /// Retrieves a CDC schema by the Avro schema's record name.
    /// </summary>
    public async Task<CDCSchema?> GetSchemaByRecordName(string recordName) {
        await using var connection = new NpgsqlConnection(_connectionString);
        
        const string sql = @"
            SELECT 
                cs.id as Id,
                cs.avro_schema_id as AvroSchemaId,
                cs.entity_name as EntityName,
                cs.db_schema_full_name as DbSchemaFullName,
                cs.soft_delete_enabled as SoftDeleteEnabled,
                cs.soft_delete_column_name as SoftDeleteColumnName,
                avro.id as Id,
                avro.schema_id as SchemaId,
                avro.record_name as RecordName,
                avro.schema_json as SchemaJson,
                avro.date_created as DateCreated,
                avro.date_updated as DateUpdated
            FROM salesforce.cdc_schemas cs
            LEFT JOIN salesforce.avro_schemas avro 
                ON cs.avro_schema_id = avro.id
            WHERE avro.record_name = @RecordName";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordName: {RecordName}", "SELECT", sql, recordName);
        }

        var res = await connection.QueryAsync<CDCSchema, DbAvroSchema, CDCSchema>(
            sql,
            (cdcSchema, avroSchema) => {
                cdcSchema.AvroSchema = avroSchema;
                return cdcSchema;
            },
            new { RecordName = recordName },
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
        // Invalidate cache
        _cache.Remove(SchemaCacheKeyPrefix);
        
        await using var connection = new NpgsqlConnection(_connectionString);
        
        const string sql = @"
            INSERT INTO salesforce.cdc_schemas (entity_name, db_schema_full_name, soft_delete_enabled, soft_delete_column_name) 
            VALUES (@EntityName, @DbSchemaFullName, @SoftDeletedEnabled, @SoftDeleteColumnName)
            RETURNING id as Id, entity_name as EntityName, db_schema_full_name as DbSchemaFullName, soft_delete_enabled as SoftDeletedEnabled, soft_delete_column_name as SoftDeleteColumnName;";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}", "INSERT", sql);
        }

        var insertedRecord = await connection.QuerySingleAsync<CDCSchema>(
            sql,
            new {
                dbSchema.EntityName,
                dbSchema.DbSchemaFullName,
                dbSchema.SoftDeletedEnabled,
                dbSchema.SoftDeleteColumnName
            }).ConfigureAwait(false);
        
        return insertedRecord;
    }

    /// <summary>
    /// Creates a new CDC schema and links it to an Avro schema.
    /// </summary>
    public async Task<CDCSchema> CreateNewSchemaWithAvroLink(CDCSchema dbSchema, int avroSchemaId) {
        // Invalidate cache
        _cache.Remove(SchemaCacheKeyPrefix);
        
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            INSERT INTO salesforce.cdc_schemas (entity_name, db_schema_full_name, soft_delete_enabled, soft_delete_column_name, avro_schema_id) 
            VALUES (@EntityName, @DbSchemaFullName, @SoftDeletedEnabled, @SoftDeleteColumnName, @AvroSchemaId)
            RETURNING id as Id, avro_schema_id as AvroSchemaId, entity_name as EntityName, db_schema_full_name as DbSchemaFullName, soft_delete_enabled as SoftDeletedEnabled, soft_delete_column_name as SoftDeleteColumnName;";

        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, AvroSchemaId: {AvroSchemaId}", "INSERT", sql, avroSchemaId);
        }

        var insertedRecord = await connection.QuerySingleAsync<CDCSchema>(
            sql,
            new {
                dbSchema.EntityName,
                dbSchema.DbSchemaFullName,
                dbSchema.SoftDeletedEnabled,
                dbSchema.SoftDeleteColumnName,
                AvroSchemaId = avroSchemaId
            }).ConfigureAwait(false);

        if (dbSchema.AvroSchema != null) {
            insertedRecord.AvroSchema = dbSchema.AvroSchema;
        }
        
        return insertedRecord;
    }

    /// <summary>
    /// Updates an existing CDC schema with a new Avro schema link.
    /// </summary>
    public async Task<bool> UpdateCdcSchemaWithAvroLink(int cdcSchemaId, int avroSchemaId) {
        // Invalidate cache
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

    public async Task<IEnumerable<CDCSchema>> GetAllSchemas(CancellationToken cancellationToken) {
        await using var connection = new NpgsqlConnection(_connectionString);
        
        const string sql = @"
            SELECT 
                cs.id as Id, 
                cs.avro_schema_id as AvroSchemaId,
                cs.entity_name as EntityName, 
                cs.db_schema_full_name as DbSchemaFullName,
                cs.soft_delete_enabled as SoftDeleteEnabled,
                cs.soft_delete_column_name as SoftDeleteColumnName,
                avro.id as Id,
                avro.schema_id as SchemaId,
                avro.record_name as RecordName,
                avro.schema_json as SchemaJson,
                avro.date_created as DateCreated,
                avro.date_updated as DateUpdated
            FROM salesforce.cdc_schemas cs
            LEFT JOIN salesforce.avro_schemas avro ON cs.avro_schema_id = avro.id
            ORDER BY cs.entity_name";

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
