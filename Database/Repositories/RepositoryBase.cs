using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace Database.Repositories;

public abstract class RepositoryBase : IRepository {
    protected readonly ILogger<RepositoryBase> _logger;
    protected readonly string _connectionString;
    protected readonly bool _debugQuery;

    /// <remarks>
    /// Takes the assembled connection string rather than <c>IConfiguration</c>: the Target Connection is
    /// stored in the App Database and assembled by an engine profile, so a repository never knows where its
    /// string came from and never reads configuration itself.
    /// </remarks>
    protected RepositoryBase(ILogger<RepositoryBase> logger, string connectionString, bool debugQuery) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _logger = logger;
        _connectionString = connectionString;
        _debugQuery = debugQuery;
    }
    
    public abstract TargetDatabaseEngine Engine { get; }

    #region Data Queries

    public abstract Task<int> Create(string table, Dictionary<string, object> data,
        CancellationToken cancellationToken = default);
    public abstract Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data);
    public abstract Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds);
    public abstract Task<int> SoftDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds);
    public abstract Task<int> UnDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds);
    #endregion
    
    #region Metadata Queries
    public abstract Task<TableMetadata?> GetTableMetadata(string tableName, string schemaName = "public",
        CancellationToken cancellationToken = default);
    public abstract Task<List<TableMetadata>> GetSchemaMetadata(string schemaName = "public",
        CancellationToken cancellationToken = default);
    public abstract Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string schemaName = "public");
    #endregion
}