using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// This is the interface for the data repository that will handle the actual data operations (CRUD) for the change events.
/// </summary>
public interface IRepository {
    #region Data Queries
    Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data);
    Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds);
    Task<int> UnDelete(string table, List<string> recordIds);
    #endregion
    
    #region Meta Queries
    Task<TableMetadata?> GetTableMetadata(string tableName, string schemaName = "public",
        CancellationToken cancellationToken = default);
    Task<List<TableMetadata>> GetSchemaMetadata(string schemaName = "public",
        CancellationToken cancellationToken = default);
    Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string schemaName = "public");
    #endregion
}