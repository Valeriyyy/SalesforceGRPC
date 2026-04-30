using System.Diagnostics.CodeAnalysis;
using Database.Repositories;
using Database.Repositories.DbDataRepositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SalesforceGrpc.Extensions;

/// <summary>
/// AOT-safe configuration extension for database repository registration.
/// This replaces the factory pattern with explicit DI registration to ensure
/// all types are available for AOT compilation and trimming.
/// </summary>
public static class AotConfiguration {
    /// <summary>
    /// Explicitly register the data repository based on TargetingDatabaseType config.
    /// This avoids reflection-based factory creation and is AOT-compatible.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", 
        Justification = "This method is essential for DI and will be preserved by the AOT compiler.")]
    public static IServiceCollection AddAotDataRepository(this IServiceCollection services, IConfiguration config) {
        var targetingDbType = config.GetValue<string>("TargetingDatabaseType") 
            ?? throw new InvalidOperationException("TargetingDatabaseType is not configured in appsettings.json");

        // Use explicit registration instead of factory to maintain type metadata for AOT
        // This ensures all repository implementations remain in the trimmed assembly
        if (!Enum.TryParse<DbType>(targetingDbType, ignoreCase: true, out var dbType)) {
            throw new InvalidOperationException(
                $"Invalid TargetingDatabaseType '{targetingDbType}'. Supported types: {string.Join(", ", Enum.GetNames(typeof(DbType)))}");
        }

        // Register the appropriate repository based on enum value
        // Note: For AOT, we register each repository type that could be used
        // The AOT compiler will keep all of them in the assembly metadata
        _ = dbType switch {
            DbType.Postgres => services.AddSingleton<IDataRepository>(sp => 
                new PostgresDataRepository(
                    sp.GetRequiredService<ILogger<PostgresDataRepository>>(),
                    config)),
            
            DbType.SqlServer => services.AddSingleton<IDataRepository>(sp => 
                new SqlServerDataRepository(
                    sp.GetRequiredService<ILogger<SqlServerDataRepository>>(),
                    config)),
            
            DbType.MySql => services.AddSingleton<IDataRepository>(sp => 
                new MySqlDataRepository(
                    sp.GetRequiredService<ILogger<MySqlDataRepository>>(),
                    config)),
            
            DbType.SqlLite => services.AddSingleton<IDataRepository>(sp => 
                new SqlLiteDataRepository(
                    sp.GetRequiredService<ILogger<SqlLiteDataRepository>>(),
                    config)),
            
            _ => throw new InvalidOperationException($"Unsupported database type: {dbType}")
        };

        return services;
    }
}



