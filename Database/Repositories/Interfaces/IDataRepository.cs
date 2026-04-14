namespace Database.Repositories.Interfaces;

/// <summary>
/// This is the interface for the data repository that will handle the actual data operations (CRUD) for the change events.
/// </summary>
public interface IDataRepository {
    Task Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    Task Update(string table, List<string> recordIds, Dictionary<string, object> data);
    Task<int> Delete(string table, List<string> recordIds);
}