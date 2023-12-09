using Database;

namespace SalesforceGrpc.Database;
public interface IPGRepository {
    Task<IEnumerable<MappedField>> GetAllMappedFieldsAsync(string entityName, CancellationToken cancellationToken);
    Task InsertNewRecord(ICollection<KeyValuePair<string, object>> recordToInsert, CancellationToken cancellationToken);
}
