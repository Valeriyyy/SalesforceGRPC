using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Database.Repositories.DbDataRepositories;

public class MySqlDataRepository : DataRepositoryBase {
    public MySqlDataRepository(ILogger<MySqlDataRepository> logger, IConfiguration configuration) : base(logger, configuration) { }

    public override Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        throw new NotImplementedException();
    }

    public override Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data) {
        throw new NotImplementedException();
    }

    public override Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds) {
        throw new NotImplementedException();
    }

    public override Task<int> UnDelete(string table, List<string> recordIds) {
        throw new NotImplementedException();
    }

    public Task<int> Delete(string table, List<string> recordIds) {
        throw new NotImplementedException();
    }
}