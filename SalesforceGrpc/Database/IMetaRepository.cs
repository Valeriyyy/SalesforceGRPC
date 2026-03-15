using Database;

namespace SalesforceGrpc.Database;
public interface IMetaRepository {
    Task Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    Task Update(string table, List<string> recordIds, Dictionary<string, object> data, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> GetCachedMapping(int? schemaId, CancellationToken cancellationToken);
    Task<IEnumerable<MappedField>> GetAllMappedFieldsAsync(int? schemaId, CancellationToken cancellationToken);
    Task<List<CDCSchema>> GetCachedSchemas(CancellationToken cancellationToken);
    Task<IEnumerable<CDCSchema>> GetCDCSchemas(CancellationToken cancellationToken);
    Task InsertNewRecord(ICollection<KeyValuePair<string, object>> recordToInsert, CancellationToken cancellationToken);
}
