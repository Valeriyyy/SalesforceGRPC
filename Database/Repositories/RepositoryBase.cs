using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Database.Repositories;

public abstract class RepositoryBase : IRepository {
    protected readonly ILogger<RepositoryBase> _logger;
    protected readonly string _connectionString;
    protected readonly bool _debugQuery = false;

    protected RepositoryBase(ILogger<RepositoryBase> logger, IConfiguration configuration) {
        _logger = logger;
        if (configuration.GetConnectionString("targetingDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("targetingDatabase")!;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
    }
    
    #region Data Queries

    public abstract Task<int> Create(string table, Dictionary<string, object> data,
        CancellationToken cancellationToken = default);
    public abstract Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data);
    public abstract Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds);
    public abstract Task<int> UnDelete(string table, List<string> recordIds);
    #endregion
    
    #region Metadata Queries
    public abstract Task<TableMetadata?> GetTableMetadata(string tableName, string schemaName = "public",
        CancellationToken cancellationToken = default);
    public abstract Task<List<TableMetadata>> GetSchemaMetadata(string schemaName = "public",
        CancellationToken cancellationToken = default);
    public abstract Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string schemaName = "public");
    #endregion
}