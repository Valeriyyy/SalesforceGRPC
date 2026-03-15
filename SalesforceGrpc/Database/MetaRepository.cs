using Dapper;
using Database;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using SqlKata.Execution;

namespace SalesforceGrpc.Database;
public class MetaRepository : IMetaRepository {
    private readonly QueryFactory _db;
    private readonly IMemoryCache _cache;
    private readonly string _connectionString;
    private const string MappingCacheKeyPrefix = "mapping_";
    private const string SchemaCacheKeyPrefix = "schemas";

    public MetaRepository(QueryFactory db, IMemoryCache cache, IConfiguration configuration) {
        _db = db;
        _cache = cache;
        if(configuration.GetConnectionString("postgres") is null) {
            throw new InvalidOperationException("Postgres connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("postgres")!;
    }
    
    public async Task Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, data).ConfigureAwait(false);
    }

    public async Task Update(string table, List<string> recordIds, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        await _db.Query(table).WhereIn("sf_id", recordIds)
            .UpdateAsync(data, cancellationToken: cancellationToken);
    }

    public async Task<Dictionary<string, string>> GetCachedMapping(int? schemaId, CancellationToken cancellationToken) {
        if (schemaId is null) {
            return new Dictionary<string, string>();
        }
        
        string cacheKey = $"{MappingCacheKeyPrefix}{schemaId}";
        
        // Try to get from cache
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? cachedMapping)) {
            return cachedMapping!;
        }
        
        // Fetch mappings if not in cache
        var mappings = await GetAllMappedFieldsAsync(schemaId, cancellationToken);

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

    public Task<IEnumerable<MappedField>> GetAllMappedFieldsAsync(int? schemaId, CancellationToken cancellationToken) {
        return _db
                .Query("salesforce.mapped_fields")
                .Select(
                    "id as Id",
                    "schema_id as SchemaId",
                    "salesforce_field_name as SalesforceFieldName",
                    "postgres_field_name as PostgresFieldName")
                .Where("schema_id", schemaId)
                .GetAsync<MappedField>(null, null, cancellationToken);
    }

    public async Task<List<CDCSchema>> GetCachedSchemas(CancellationToken cancellationToken) {
        // Try to get from cache
        if (_cache.TryGetValue(SchemaCacheKeyPrefix, out List<CDCSchema>? cachedSchemas)) {
            return cachedSchemas!;
        }
        
        var schemas = (await GetCDCSchemas(cancellationToken)).ToList();

        // Store in cache with a 1-hour sliding expiration
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromHours(1));
        _cache.Set(SchemaCacheKeyPrefix, schemas, cacheOptions);
        
        return schemas.ToList();
    }

    public async Task<IEnumerable<CDCSchema>> GetCDCSchemas(CancellationToken cancellationToken = default) {
        return await _db.Query("salesforce.cdc_schemas")
            .Select("id as Id", "entity_name as EntityName", "schema_id as SchemaId", 
                "schema_name as SchemaName", "db_schema_full_name as DbSchemaFullName")
            .GetAsync<CDCSchema>(cancellationToken: cancellationToken);
    }

    public Task InsertNewRecord(ICollection<KeyValuePair<string, object>> recordToInsert, CancellationToken cancellationToken) {
        return _db.Query("salesforce.accounts").InsertAsync(recordToInsert, null, null, cancellationToken);
    }
}
