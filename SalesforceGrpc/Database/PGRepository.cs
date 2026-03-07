using Database;
using SqlKata.Execution;
using System.Threading;

namespace SalesforceGrpc.Database;
public class PGRepository : IPGRepository {
    private readonly QueryFactory _db;
    private readonly Dictionary<string, Dictionary<string, string>> _mappingCache = new();
    private readonly object _lockObj = new();

    public PGRepository(QueryFactory db) {
        _db = db;
    }

    public async Task ExecuteQuery(string table, List<string> recordIds, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        await _db.Query(table).WhereIn("sf_id", recordIds)
            .UpdateAsync(data, cancellationToken: cancellationToken);
    }

    public async Task<Dictionary<string, string>> GetCachedMapping(string entityName, CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(entityName)) {
            return new Dictionary<string, string>();
        }
        
        // Check cache without async blocking
        lock (_lockObj) {
            if (_mappingCache.TryGetValue(entityName, out var cachedMapping)) {
                return cachedMapping;
            }
        }
        
        // Fetch mappings if not in cache
        var mappings = await GetAllMappedFieldsAsync(entityName, cancellationToken);

        // Build the mapping dictionary from the provided mappings
        var mapping = mappings
            .Where(m => m.EntityName == entityName)
            .ToDictionary(m => m.SalesforceFieldName, m => m.PostgresFieldName);

        // Store in cache
        lock (_lockObj) {
            _mappingCache[entityName] = mapping;
        }
        
        return mapping;
    }

    public Task<IEnumerable<MappedField>> GetAllMappedFieldsAsync(string entityName, CancellationToken cancellationToken) {
        return _db
                .Query("salesforce.mapped_fields")
                .Select(
                    "id as Id",
                    "entity_name as EntityName",
                    "salesforce_field_name as SalesforceFieldName",
                    "postgres_field_name as PostgresFieldName")
                .Where("entity_name", entityName)
                .GetAsync<MappedField>(null, null, cancellationToken);
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
