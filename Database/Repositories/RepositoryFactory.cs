using Database.Models;
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
        if (!Enum.TryParse<TargetDatabaseEngine>(databaseType, true, out var dbType)) {
            throw new InvalidOperationException(
                $"Invalid TargetingDatabaseType '{databaseType}'. Supported types: {string.Join(", ", Enum.GetNames(typeof(TargetDatabaseEngine)))}");
        }

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        return dbType switch {
            TargetDatabaseEngine.Postgres => new PostgresRepository(
                serviceProvider.GetRequiredService<ILogger<PostgresRepository>>(),
                configuration),
            
            TargetDatabaseEngine.SqlServer => new SqlServerRepository(
                serviceProvider.GetRequiredService<ILogger<SqlServerRepository>>(),
                configuration),
            
            TargetDatabaseEngine.MySql => new MySqlRepository(
                serviceProvider.GetRequiredService<ILogger<MySqlRepository>>(),
                configuration),
            
            TargetDatabaseEngine.Sqlite => new SqliteRepository(
                serviceProvider.GetRequiredService<ILogger<SqliteRepository>>(),
                configuration),
            
            _ => throw new InvalidOperationException($"Unsupported database type: {dbType}")
        };
    }
}


