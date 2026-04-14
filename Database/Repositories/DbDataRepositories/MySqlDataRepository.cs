using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Database.Repositories.DbDataRepositories;

public class MySqlDataRepository : IDataRepository {
    private readonly ILogger<MySqlDataRepository> _logger;
    private readonly string _connectionString;

    public MySqlDataRepository(ILogger<MySqlDataRepository> logger, IConfiguration configuration) {
        _logger = logger;
        if (configuration.GetConnectionString("targetingDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("targetingDatabase")!;
    }

    public Task Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        throw new NotImplementedException();
    }

    public Task Update(string table, List<string> recordIds, Dictionary<string, object> data) {
        throw new NotImplementedException();
    }

    public Task<int> Delete(string table, List<string> recordIds) {
        throw new NotImplementedException();
    }
}