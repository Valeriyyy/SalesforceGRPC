using Database.Models;
using Database.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace SalesforceGRPCTest;

/// <summary>
/// Integration tests for the SQL Server metadata queries, against a real database.
/// </summary>
/// <remarks>
/// The connection string comes from the <c>SALESFORCEGRPC_TEST_SQLSERVER_TARGET_DATABASE</c> environment
/// variable, and every test skips when it is unset. A failure here says something about the database that
/// was reachable, not about the code under test.
/// </remarks>
public class SqlServerMetadataRepositoryTests {
    private const string ConnectionStringVariable = "SALESFORCEGRPC_TEST_SQLSERVER_TARGET_DATABASE";

    private static SqlServerRepository Repository() {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
            $"Set {ConnectionStringVariable} to a SQL Server connection string to run this test.");

        return new SqlServerRepository(NullLogger<SqlServerRepository>.Instance, connectionString!, debugQuery: false);
    }

    [Fact(DisplayName = "Returns null for non-existent table")]
    public async Task GetTableMetadata_WithInvalidTable_ReturnsNull() {
        var metadata = await Repository().GetTableMetadata("nonexistent_table_xyz_123", "dbo");
        Assert.Null(metadata);
    }

    [Fact(DisplayName = "Can retrieve schema metadata")]
    public async Task GetSchemaMetadata_ReturnsAllTables() {
        var metadata = await Repository().GetSchemaMetadata("dbo");

        Assert.NotNull(metadata);
        Assert.IsType<List<TableMetadata>>(metadata);
    }

    /// <summary>
    /// A caller that expresses "no schema" (null) must reach the same table SQL Server's own default
    /// schema does — the shared IRepository interface no longer defaults to "dbo" itself; SqlServerRepository
    /// does.
    /// </summary>
    [Fact(DisplayName = "Null schema defaults to dbo for schema metadata")]
    public async Task GetSchemaMetadata_WithNullSchema_DefaultsToDbo() {
        var repository = Repository();

        var withNull = await repository.GetSchemaMetadata(null);
        var withDbo = await repository.GetSchemaMetadata("dbo");

        Assert.Equal(withDbo.Select(t => t.TableName).OrderBy(n => n),
            withNull.Select(t => t.TableName).OrderBy(n => n));
    }
}
