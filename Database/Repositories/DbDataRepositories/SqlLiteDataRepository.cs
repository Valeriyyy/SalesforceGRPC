using Dapper;
using Database.Repositories.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Database.Repositories.DbDataRepositories;

public class SqlLiteDataRepository : DataRepositoryBase {
    public SqlLiteDataRepository(ILogger<DataRepositoryBase> logger, IConfiguration configuration) : base(logger, configuration) {
        
    }

    public override async Task Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "CREATE", sql, data);
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(sql, data).ConfigureAwait(false);
    }

    public override Task Update(string table, List<string> recordIds, Dictionary<string, object> data) {
        throw new NotImplementedException();
    }

    public override Task<int> Delete(string table, List<string> recordIds) {
        throw new NotImplementedException();
    }
}