using Dapper;
using Database.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Repositories;

public class PostgresRepository : RepositoryBase {
    public PostgresRepository(ILogger<RepositoryBase> logger, IConfiguration configuration) : base(logger, configuration) { }

    #region Data Queries
    public override async Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "CREATE", sql, data);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        var resultCount = await connection.ExecuteAsync(sql, data).ConfigureAwait(false);
        
        return resultCount;
    }
    
    public override async Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data) {
        var setClause = string.Join(", ", data.Keys.Select(k => $"{k} = @{k}"));
        var sql = $"UPDATE {table} SET {setClause} WHERE {sfFieldMapping} = ANY(@RecordIds)";
        
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}, RecordIds: {@RecordIds}", "UPDATE", sql, data, recordIds);
        }

        var parameters = new DynamicParameters(data);
        parameters.Add("RecordIds", recordIds.ToArray());

        await using var connection = new NpgsqlConnection(_connectionString);
        var resultCount = await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);

        return resultCount;
    }

    public override async Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds) {
        var sql = $"DELETE FROM {table} WHERE {sfIdColumnName} = ANY(@RecordIds)";
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

    public override Task<int> UnDelete(string table, List<string> recordIds) {
        throw new NotImplementedException();
    }
    #endregion
    
    
    #region Meta Queries
    /// <summary>
    /// Retrieves complete metadata for a specific table including columns and constraints.
    /// </summary>
    public override async Task<TableMetadata?> GetTableMetadata(string tableName, string schemaName = "public", CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var columns = await GetTableColumns(connection, schemaName, tableName, cancellationToken).ConfigureAwait(false);
        var constraints = await GetTableConstraints(connection, schemaName, tableName, cancellationToken).ConfigureAwait(false);

        if (!columns.Any()) {
            return null; // Table doesn't exist
        }

        return new TableMetadata {
            SchemaName = schemaName,
            TableName = tableName,
            Columns = columns,
            Constraints = constraints
        };
    }

    /// <summary>
    /// Retrieves metadata for all tables in a schema.
    /// </summary>
    public override async Task<List<TableMetadata>> GetSchemaMetadata(string schemaName = "public", CancellationToken cancellationToken = default) {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = @SchemaName
            AND table_type = 'BASE TABLE'
            ORDER BY table_name";

        var tableNames = await connection.QueryAsync<string>(sql, new { SchemaName = schemaName }).ConfigureAwait(false);
        var tableMetadataList = new List<TableMetadata>();

        foreach (var tableName in tableNames) {
            var metadata = await GetTableMetadata(tableName, schemaName, cancellationToken).ConfigureAwait(false);
            if (metadata != null) {
                tableMetadataList.Add(metadata);
            }
        }

        return tableMetadataList;
    }

    /// <summary>
    /// Retrieves columns for a specific table with type and nullability information.
    /// </summary>
    private async Task<List<ColumnMetadata>> GetTableColumns(NpgsqlConnection connection, string schemaName, string tableName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT 
                column_name as ColumnName,
                data_type as DataType,
                (is_nullable = 'YES') as IsNullable,
                column_default as DefaultValue,
                ordinal_position as OrdinalPosition
            FROM information_schema.columns
            WHERE table_schema = @SchemaName
            AND table_name = @TableName
            ORDER BY ordinal_position";

        var columns = (await connection.QueryAsync<ColumnMetadata>(
            sql,
            new { SchemaName = schemaName, TableName = tableName },
            commandTimeout: 30
        ).ConfigureAwait(false)).ToList();

        // Enrich each column with its constraints
        foreach (var column in columns) {
            column.ColumnConstraints = await GetColumnConstraints(connection, schemaName, tableName, column.ColumnName, cancellationToken).ConfigureAwait(false);
        }

        return columns;
    }

    /// <summary>
    /// Retrieves all constraints for a specific table.
    /// </summary>
    private async Task<List<ConstraintMetadata>> GetTableConstraints(NpgsqlConnection connection, string schemaName, string tableName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT 
                tc.constraint_name as ConstraintName,
                tc.constraint_type as ConstraintType,
                string_agg(kcu.column_name, ', ') as ColumnList,
                ccu.table_name as ReferencedTableName,
                ccu.column_name as ReferencedColumnName
            FROM information_schema.table_constraints tc
            LEFT JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            LEFT JOIN information_schema.constraint_column_usage ccu
                ON tc.constraint_name = ccu.constraint_name
                AND tc.table_schema = ccu.table_schema
            WHERE tc.table_schema = @SchemaName
            AND tc.table_name = @TableName
            GROUP BY tc.constraint_name, tc.constraint_type, ccu.table_name, ccu.column_name
            ORDER BY tc.constraint_type, tc.constraint_name";

        var constraintRows = (await connection.QueryAsync<dynamic>(
            sql,
            new { SchemaName = schemaName, TableName = tableName },
            commandTimeout: 30
        ).ConfigureAwait(false)).ToList();

        var constraints = new List<ConstraintMetadata>();
        foreach (var row in constraintRows) {
            var columns = ((string)row.ColumnList ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();
            
            constraints.Add(new ConstraintMetadata {
                ConstraintName = row.ConstraintName,
                ConstraintType = row.ConstraintType,
                Columns = columns,
                ReferencedTableName = row.ReferencedTableName,
                ReferencedColumns = string.IsNullOrEmpty(row.ReferencedColumnName)
                    ? []
                    : [row.ReferencedColumnName]
            });
        }

        return constraints;
    }

    /// <summary>
    /// Retrieves constraints specific to a single column.
    /// </summary>
    private async Task<List<ColumnConstraint>> GetColumnConstraints(NpgsqlConnection connection, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT 
                tc.constraint_type as ConstraintType,
                tc.constraint_name as ConstraintName,
                ccu.table_name as ReferencedTable,
                ccu.column_name as ReferencedColumn
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            LEFT JOIN information_schema.constraint_column_usage ccu
                ON tc.constraint_name = ccu.constraint_name
                AND tc.table_schema = ccu.table_schema
            WHERE tc.table_schema = @SchemaName
            AND tc.table_name = @TableName
            AND kcu.column_name = @ColumnName
            ORDER BY tc.constraint_type";

        return (await connection.QueryAsync<ColumnConstraint>(
            sql,
            new { SchemaName = schemaName, TableName = tableName, ColumnName = columnName },
            commandTimeout: 30
        ).ConfigureAwait(false)).ToList();
    }

    /// <summary>
    /// Retrieves only the foreign key relationships for a table.
    /// </summary>
    public override async Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string schemaName = "public") {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = @"
            SELECT 
                tc.constraint_name as ConstraintName,
                'FOREIGN KEY' as ConstraintType,
                string_agg(kcu.column_name, ', ') as ColumnList,
                ccu.table_name as ReferencedTableName,
                string_agg(ccu.column_name, ', ') as ReferencedColumnList
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
                ON tc.constraint_name = ccu.constraint_name
                AND tc.table_schema = ccu.table_schema
            WHERE tc.table_schema = @SchemaName
            AND tc.table_name = @TableName
            AND tc.constraint_type = 'FOREIGN KEY'
            GROUP BY tc.constraint_name, ccu.table_name
            ORDER BY tc.constraint_name";

        var foreignKeyRows = (await connection.QueryAsync<dynamic>(
            sql,
            new { SchemaName = schemaName, TableName = tableName },
            commandTimeout: 30
        ).ConfigureAwait(false)).ToList();

        var foreignKeys = new List<ConstraintMetadata>();
        foreach (var row in foreignKeyRows) {
            var columns = ((string)row.ColumnList ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();
            var refColumns = ((string)row.ReferencedColumnList ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();
            
            foreignKeys.Add(new ConstraintMetadata {
                ConstraintName = row.ConstraintName,
                ConstraintType = "FOREIGN KEY",
                Columns = columns,
                ReferencedTableName = row.ReferencedTableName,
                ReferencedColumns = refColumns
            });
        }

        return foreignKeys;
    }
    #endregion
}