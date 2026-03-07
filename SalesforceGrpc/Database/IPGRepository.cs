using Database;

namespace SalesforceGrpc.Database;
public interface IPGRepository {
    Task ExecuteQuery(string table, List<string> recordIds, Dictionary<string, object> data, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> GetCachedMapping(string entityName, CancellationToken cancellationToken);
    Task<IEnumerable<MappedField>> GetAllMappedFieldsAsync(string entityName, CancellationToken cancellationToken);
    Task<IEnumerable<CDCSchema>> GetCDCSchemas(CancellationToken cancellationToken);
    Task InsertNewRecord(ICollection<KeyValuePair<string, object>> recordToInsert, CancellationToken cancellationToken);
}
