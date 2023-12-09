using Database;
using SqlKata.Execution;
using System.Threading;

namespace SalesforceGrpc.Database;
public class PGRepository : IPGRepository {
    private readonly QueryFactory _db;

    public PGRepository(QueryFactory db) {
        _db = db;
    }

    public Task<IEnumerable<MappedField>> GetAllMappedFieldsAsync(string entityName, CancellationToken cancellationToken) {
        /*return _db
                .Query("salesforce.mapped_fields")
                .Select(
                    "id as Id",
                    "schema_id as SchemaId",
                    "salesforce_field_name as SalesforceFieldName",
                    "postgres_field_name as PostgresFieldName")
                .GetAsync<MappedField>(null, null, cancellationToken);*/

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

    public Task InsertNewRecord(ICollection<KeyValuePair<string, object>> recordToInsert, CancellationToken cancellationToken) {
        return _db.Query("salesforce.accounts").InsertAsync(recordToInsert, null, null, cancellationToken);
    }
}
