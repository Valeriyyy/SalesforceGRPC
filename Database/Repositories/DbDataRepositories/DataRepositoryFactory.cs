using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Database.Repositories.DbDataRepositories;

/// <summary>
/// Factory for creating the appropriate data repository based on the configured database type.
/// </summary>
public class DataRepositoryFactory {
    public static IDataRepository Create(string databaseType, IServiceProvider serviceProvider) {
        if (!Enum.TryParse<DbType>(databaseType, true, out var dbType)) {
            throw new InvalidOperationException(
                $"Invalid TargetingDatabaseType '{databaseType}'. Supported types: {string.Join(", ", Enum.GetNames(typeof(DbType)))}");
        }

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        return dbType switch {
            DbType.Postgres => new PostgresDataRepository(
                serviceProvider.GetRequiredService<ILogger<PostgresDataRepository>>(),
                configuration),
            
            DbType.SqlServer => new SqlServerDataRepository(
                serviceProvider.GetRequiredService<ILogger<SqlServerDataRepository>>(),
                configuration),
            
            DbType.MySql => new MySqlDataRepository(
                serviceProvider.GetRequiredService<ILogger<MySqlDataRepository>>(),
                configuration),
            
            DbType.SqlLite => new SqlLiteDataRepository(
                serviceProvider.GetRequiredService<ILogger<SqlLiteDataRepository>>(),
                configuration),
            
            _ => throw new InvalidOperationException($"Unsupported database type: {dbType}")
        };
    }
}


