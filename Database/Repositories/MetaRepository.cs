using Dapper;
using Database.Models;
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
        if(configuration.GetConnectionString("postgres") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("postgres")!;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
    }
    
    public async Task Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "CREATE", sql, data);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, data).ConfigureAwait(false);
    }
    
    public async Task Update(string table, List<string> recordIds, Dictionary<string, object> data) {
        var setClause = string.Join(", ", data.Keys.Select(k => $"{k} = @{k}"));
        var sql = $"UPDATE {table} SET {setClause} WHERE sf_id = ANY(@RecordIds)";
        
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}, RecordIds: {@RecordIds}", "UPDATE", sql, data, recordIds);
        }

        var parameters = new DynamicParameters(data);
        parameters.Add("RecordIds", recordIds.ToArray());

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
    }

    public async Task<int> Delete(string table, List<string> recordIds) {
        var sql = $"DELETE FROM {table} WHERE sf_id = ANY(@RecordIds)";
        // var sql = $"UPDATE {table} SET is_deleted = true WHERE sf_id = ANY(@RecordIds)";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordIds: {@RecordIds}", "DELETE", sql, recordIds);
        }

        var parameters = new DynamicParameters();
        parameters.Add("RecordIds", recordIds.ToArray());

        using var result = new NpgsqlConnection(_connectionString)
            .ExecuteAsync(sql, parameters);
        return result.Result;
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
            .ToDictionary(m => m.SalesforceFieldName, m => m.PostgresFieldName);

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
            postgres_field_name as PostgresFieldName
            FROM salesforce.mapped_fields WHERE schema_id = @SchemaId",
            new { SchemaId = schemaId }).ConfigureAwait(false);
    }

    public async Task<List<CDCSchema>> GetCachedSchemas(CancellationToken cancellationToken) {
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
        
        var insertedRecord = await connection.QuerySingleAsync<CDCSchema>(
            @"INSERT INTO salesforce.cdc_schemas (entity_name, schema_id, schema_name, db_schema_full_name) 
              VALUES (@EntityName, @SchemaId, @SchemaName, @DbSchemaFullName)
                RETURNING *;",
            new {
                dbSchema.EntityName,
                dbSchema.SchemaId,
                dbSchema.SchemaName,
                dbSchema.DbSchemaFullName
            }).ConfigureAwait(false);
        
        return insertedRecord;
    }

    public async Task<IEnumerable<CDCSchema>> GetAllSchemas(CancellationToken cancellationToken) {
        await using var connection = new NpgsqlConnection(_connectionString);
        
        var res = await connection.QueryAsync<CDCSchema>(
            "SELECT id as Id, entity_name as EntityName, schema_id as SchemaId, schema_name as SchemaName, db_schema_full_name as DbSchemaFullName " +
            "FROM salesforce.cdc_schemas"
        , cancellationToken).ConfigureAwait(false);

        return res;
    }
}
