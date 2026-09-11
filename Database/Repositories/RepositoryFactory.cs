using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Database.Repositories;

/// <summary>
/// Builds the repository from appsettings.json. Superseded by the engine profiles and the Target Connection
/// provider; kept only until the composition root stops reading configuration for this.
/// </summary>
public static class RepositoryFactory {
    public static IRepository Create(string databaseType, IServiceProvider serviceProvider) {
        if (!Enum.TryParse<TargetDatabaseEngine>(databaseType, true, out var engine)) {
            throw new InvalidOperationException(
                $"Invalid TargetingDatabaseType '{databaseType}'. Supported types: {string.Join(", ", Enum.GetNames(typeof(TargetDatabaseEngine)))}");
        }

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("targetingDatabase")
            ?? throw new InvalidOperationException("Db connection string is not configured.");
        var debugQuery = configuration.GetValue<bool>("DebugQuery");
        var loggers = serviceProvider.GetRequiredService<ILoggerFactory>();

        return engine switch {
            TargetDatabaseEngine.Postgres => new PostgresRepository(loggers.CreateLogger<PostgresRepository>(), connectionString, debugQuery),
            TargetDatabaseEngine.SqlServer => new SqlServerRepository(loggers.CreateLogger<SqlServerRepository>(), connectionString, debugQuery),
            TargetDatabaseEngine.MySql => new MySqlRepository(loggers.CreateLogger<MySqlRepository>(), connectionString, debugQuery),
            TargetDatabaseEngine.Sqlite => new SqliteRepository(loggers.CreateLogger<SqliteRepository>(), connectionString, debugQuery),
            _ => throw new InvalidOperationException($"Unsupported database type: {engine}")
        };
    }
}
