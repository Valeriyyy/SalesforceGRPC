using Dapper;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Repositories.DbDataRepositories;

public class PostgresDataRepository : IDataRepository {
    private readonly ILogger<PostgresDataRepository> _logger;
    private readonly string _connectionString;
    private readonly bool _debugQuery = false;

    public PostgresDataRepository(ILogger<PostgresDataRepository> logger, IConfiguration configuration) {
        _logger = logger;
        if(configuration.GetConnectionString("targetingDatabase") is null) {
            throw new InvalidOperationException("Db connection string is not configured.");
        }
        _connectionString = configuration.GetConnectionString("targetingDatabase")!;
        _debugQuery = configuration.GetValue<bool>("DebugQuery");
    }

    public async Task Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "CREATE", sql, data);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, data).ConfigureAwait(false);
    }
    
    public async Task Update(string table, List<string> recordIds, Dictionary<string, object> data) {
        var setClause = string.Join(", ", data.Keys.Select(k => $"{k} = @{k}"));
        var sql = $"UPDATE {table} SET {setClause} WHERE sf_id = ANY(@RecordIds)";
        
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}, RecordIds: {@RecordIds}", "UPDATE", sql, data, recordIds);
        }

        var parameters = new DynamicParameters(data);
        parameters.Add("RecordIds", recordIds.ToArray());

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
    }

    public async Task<int> Delete(string table, List<string> recordIds) {
        var sql = $"DELETE FROM {table} WHERE sf_id = ANY(@RecordIds)";
        // var sql = $"UPDATE {table} SET is_deleted = true WHERE sf_id = ANY(@RecordIds)";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordIds: {@RecordIds}", "DELETE", sql, recordIds);
        }

        var parameters = new DynamicParameters();
        parameters.Add("RecordIds", recordIds.ToArray());

        using var result = new NpgsqlConnection(_connectionString)
            .ExecuteAsync(sql, parameters);
        return result.Result;
    }
}