using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// This is the interface for the data repository that will handle the actual data operations (CRUD) for the change events.
/// </summary>
public interface IRepository {
    /// <summary>
    /// Which dialect this repository speaks. Type Compatibility is keyed by it, and the Binding API uses it to
    /// report a clear error for a driver that is not implemented rather than surfacing NotImplementedException.
    /// </summary>
    DbType DatabaseType { get; }

    #region Data Queries
    Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default);
    Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data);
    Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds);

    /// <summary>
    /// Marks rows deleted instead of removing them, for a Binding with soft delete enabled.
    /// </summary>
    Task<int> SoftDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds);

    /// <summary>
    /// Clears the soft delete flag, restoring rows an UNDELETE event refers to.
    /// </summary>
    /// <remarks>
    /// Only meaningful for a Binding with soft delete enabled — a hard-deleted row is gone and cannot be
    /// restored from a change event, which carries no field values for the record.
    /// </remarks>
    Task<int> UnDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds);
    #endregion

    #region Meta Queries
    Task<TableMetadata?> GetTableMetadata(string tableName, string schemaName = "public",
        CancellationToken cancellationToken = default);
    Task<List<TableMetadata>> GetSchemaMetadata(string schemaName = "public",
        CancellationToken cancellationToken = default);
    Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string schemaName = "public");
    #endregion
}
