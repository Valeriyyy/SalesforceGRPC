using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Database.Repositories;

/// <summary>
/// Factory for creating the appropriate data repository based on the configured database type.
/// </summary>
public static class RepositoryFactory {
    public static IRepository Create(string databaseType, IServiceProvider serviceProvider) {
        if (!Enum.TryParse<DbType>(databaseType, true, out var dbType)) {
            throw new InvalidOperationException(
                $"Invalid TargetingDatabaseType '{databaseType}'. Supported types: {string.Join(", ", Enum.GetNames(typeof(DbType)))}");
        }

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        return dbType switch {
            DbType.Postgres => new PostgresRepository(
                serviceProvider.GetRequiredService<ILogger<PostgresRepository>>(),
                configuration),
            
            DbType.SqlServer => new SqlServerRepository(
                serviceProvider.GetRequiredService<ILogger<SqlServerRepository>>(),
                configuration),
            
            DbType.MySql => new MySqlRepository(
                serviceProvider.GetRequiredService<ILogger<MySqlRepository>>(),
                configuration),
            
            DbType.SqlLite => new SqlLiteRepository(
                serviceProvider.GetRequiredService<ILogger<SqlLiteRepository>>(),
                configuration),
            
            _ => throw new InvalidOperationException($"Unsupported database type: {dbType}")
        };
    }
}


