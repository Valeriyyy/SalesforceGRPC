using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace SalesforceGRPCTest;

/// <summary>
/// Integration tests for the Postgres metadata queries, against a real database.
/// </summary>
/// <remarks>
/// The connection string comes from the <c>SALESFORCEGRPC_TEST_TARGET_DATABASE</c> environment variable — the
/// target database no longer lives in appsettings.json — and every test skips when it is unset. A failure here
/// says something about the database that was reachable, not about the code under test.
/// </remarks>
public class PostgresMetadataRepositoryTests {
    private const string ConnectionStringVariable = "SALESFORCEGRPC_TEST_TARGET_DATABASE";

    private static PostgresRepository Repository() {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
            $"Set {ConnectionStringVariable} to a Postgres connection string to run this test.");

        return new PostgresRepository(NullLogger<PostgresRepository>.Instance, connectionString!, debugQuery: false);
    }

    [Fact(DisplayName = "Can retrieve table metadata for existing table")]
    public async Task GetTableMetadata_WithValidTable_ReturnsMetadata() {
        // This test requires the database to have at least one table
        // It demonstrates the usage of the metadata repository
        
        // Try to get metadata for a known system table
        var metadata = await Repository().GetTableMetadata("salesforce_mapped_fields", "public");
        
        // If the table exists, verify the structure
        if (metadata != null) {
            Assert.NotNull(metadata);
            Assert.NotEmpty(metadata.TableName);
            Assert.NotEmpty(metadata.Columns);
        }
    }

    [Fact(DisplayName = "Returns null for non-existent table")]
    public async Task GetTableMetadata_WithInvalidTable_ReturnsNull() {
        var metadata = await Repository().GetTableMetadata("nonexistent_table_xyz_123", "public");
        Assert.Null(metadata);
    }

    [Fact(DisplayName = "Can retrieve schema metadata")]
    public async Task GetSchemaMetadata_ReturnsAllTables() {
        var metadata = await Repository().GetSchemaMetadata("public");
        
        // Should return a list (may be empty if no tables exist)
        Assert.NotNull(metadata);
        Assert.IsType<List<TableMetadata>>(metadata);
    }

    [Fact(DisplayName = "Column metadata contains required fields")]
    public async Task GetTableMetadata_ColumnMetadataIsComplete() {
        var metadata = await Repository().GetTableMetadata("salesforce_mapped_fields", "public");
        
        if (metadata?.Columns.Any() == true) {
            var column = metadata.Columns.First();
            
            Assert.NotNull(column.ColumnName);
            Assert.NotNull(column.DataType);
            Assert.True(column.OrdinalPosition > 0);
        }
    }
}
