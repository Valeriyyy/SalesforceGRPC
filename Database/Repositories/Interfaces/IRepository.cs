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
    TargetDatabaseEngine Engine { get; }

    #region Data Queries
    /// <summary>
    /// Inserts a row, or updates every supplied column of the row whose <paramref name="keyColumn"/> already
    /// holds the same value.
    /// </summary>
    /// <remarks>
    /// How a CREATE is written. Delivery is at-least-once (ADR 0005), so a CREATE can arrive for a record
    /// that is already there, and it has to land on that row rather than add a second one or fail. Relies on
    /// a unique constraint over <paramref name="keyColumn"/>, which the Key Mapping is required to have.
    /// </remarks>
    Task<int> Upsert(string table, string keyColumn, Dictionary<string, object> data, CancellationToken cancellationToken = default);
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
    /// <summary>
    /// Null means the caller expressed no schema. What that means is up to the implementation: an engine
    /// with schemas resolves it to its own default (Postgres: "public"); an engine without them ignores it.
    /// </summary>
    Task<TableMetadata?> GetTableMetadata(string tableName, string? schemaName = null,
        CancellationToken cancellationToken = default);
    Task<List<TableMetadata>> GetSchemaMetadata(string? schemaName = null,
        CancellationToken cancellationToken = default);
    Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string? schemaName = null);
    #endregion
}
