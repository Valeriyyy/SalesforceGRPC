using Database.Models;
using Database.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace SalesforceGRPCTest;

/// <summary>
/// Integration tests for the MySQL metadata queries, against a real database.
/// </summary>
/// <remarks>
/// The connection string comes from the <c>SALESFORCEGRPC_TEST_MYSQL_TARGET_DATABASE</c> environment
/// variable, and every test skips when it is unset. A failure here says something about the database that
/// was reachable, not about the code under test.
/// </remarks>
public class MySqlMetadataRepositoryTests {
    private const string ConnectionStringVariable = "SALESFORCEGRPC_TEST_MYSQL_TARGET_DATABASE";

    private static (MySqlRepository Repository, string Database) RepositoryAndDatabase() {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
            $"Set {ConnectionStringVariable} to a MySQL connection string to run this test.");

        var database = new MySqlConnectionStringBuilder(connectionString!).Database;
        return (new MySqlRepository(NullLogger<MySqlRepository>.Instance, connectionString!, debugQuery: false), database);
    }

    [Fact(DisplayName = "Returns null for non-existent table")]
    public async Task GetTableMetadata_WithInvalidTable_ReturnsNull() {
        var (repository, database) = RepositoryAndDatabase();

        var metadata = await repository.GetTableMetadata("nonexistent_table_xyz_123", database);
        Assert.Null(metadata);
    }

    [Fact(DisplayName = "Can retrieve schema metadata")]
    public async Task GetSchemaMetadata_ReturnsAllTables() {
        var (repository, database) = RepositoryAndDatabase();

        var metadata = await repository.GetSchemaMetadata(database);

        Assert.NotNull(metadata);
        Assert.IsType<List<TableMetadata>>(metadata);
    }

    /// <summary>
    /// MySQL has no schema separate from its database, so a caller that expresses "no schema" (null) must
    /// reach the same table the connection's own database does — MySqlRepository resolves that itself,
    /// parsed from its own connection string, rather than relying on a hardcoded literal like Postgres's
    /// "public" or SQL Server's "dbo".
    /// </summary>
    [Fact(DisplayName = "Null schema defaults to the connection's own database")]
    public async Task GetSchemaMetadata_WithNullSchema_DefaultsToTheConnectionsDatabase() {
        var (repository, database) = RepositoryAndDatabase();

        var withNull = await repository.GetSchemaMetadata(null);
        var withDatabase = await repository.GetSchemaMetadata(database);

        Assert.Equal(withDatabase.Select(t => t.TableName).OrderBy(n => n),
            withNull.Select(t => t.TableName).OrderBy(n => n));
    }
}
