using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Database.Repositories.DbDataRepositories;

public abstract class DataRepositoryBase : IDataRepository {
    protected readonly ILogger<DataRepositoryBase> _logger;
    protected readonly string _connectionString;
    protected readonly bool _debugQuery = false;

    protected DataRepositoryBase(ILogger<DataRepositoryBase> logger, IConfiguration configuration) {
        _logger = logger;
        if (configuration.GetConnectionString("targetingDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("targetingDatabase")!;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
    }

    public abstract Task Create(string table, Dictionary<string, object> data,
        CancellationToken cancellationToken = default);

    public abstract Task Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data);

    public abstract Task<int> Delete(string table, List<string> recordIds);
    
    public abstract Task<int> UnDelete(string table, List<string> recordIds);
}