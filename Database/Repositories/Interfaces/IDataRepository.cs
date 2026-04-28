namespace Database.Repositories.Interfaces;

/// <summary>
/// This is the interface for the data repository that will handle the actual data operations (CRUD) for the change events.
/// </summary>
public interface IDataRepository {
    Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data);
    Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds);
}