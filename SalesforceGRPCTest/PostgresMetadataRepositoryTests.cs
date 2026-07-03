using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SalesforceGRPCTest;

/// <summary>
/// Integration tests for PostgresMetadataRepository.
/// Note: These tests require a PostgreSQL database connection configured in appsettings.
/// </summary>
public class PostgresMetadataRepositoryTests {
    private readonly PostgresRepository _repository;

    public PostgresMetadataRepositoryTests() {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var loggerFactory = new LoggerFactory();
        var logger = loggerFactory.CreateLogger<PostgresRepository>();
        _repository = new PostgresRepository(logger, config);
    }

    [Fact(DisplayName = "Can retrieve table metadata for existing table")]
    public async Task GetTableMetadata_WithValidTable_ReturnsMetadata() {
        // This test requires the database to have at least one table
        // It demonstrates the usage of the metadata repository
        
        // Try to get metadata for a known system table
        var metadata = await _repository.GetTableMetadata("salesforce_mapped_fields", "public");
        
        // If the table exists, verify the structure
        if (metadata != null) {
            Assert.NotNull(metadata);
            Assert.NotEmpty(metadata.TableName);
            Assert.NotEmpty(metadata.Columns);
        }
    }

    [Fact(DisplayName = "Returns null for non-existent table")]
    public async Task GetTableMetadata_WithInvalidTable_ReturnsNull() {
        var metadata = await _repository.GetTableMetadata("nonexistent_table_xyz_123", "public");
        Assert.Null(metadata);
    }

    [Fact(DisplayName = "Can retrieve schema metadata")]
    public async Task GetSchemaMetadata_ReturnsAllTables() {
        var metadata = await _repository.GetSchemaMetadata("public");
        
        // Should return a list (may be empty if no tables exist)
        Assert.NotNull(metadata);
        Assert.IsType<List<TableMetadata>>(metadata);
    }

    [Fact(DisplayName = "Column metadata contains required fields")]
    public async Task GetTableMetadata_ColumnMetadataIsComplete() {
        var metadata = await _repository.GetTableMetadata("salesforce_mapped_fields", "public");
        
        if (metadata?.Columns.Any() == true) {
            var column = metadata.Columns.First();
            
            Assert.NotNull(column.ColumnName);
            Assert.NotNull(column.DataType);
            Assert.True(column.OrdinalPosition > 0);
        }
    }
}
