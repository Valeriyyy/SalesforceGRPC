using Dapper;
using Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Database.Repositories;

public class SqlLiteRepository : RepositoryBase {
    public SqlLiteRepository(ILogger<RepositoryBase> logger, IConfiguration configuration) : base(logger, configuration) { }

    public override async Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "CREATE", sql, data);
        }

        await using var connection = new SqliteConnection(_connectionString);
        var resultCount = await connection.ExecuteAsync(sql, data).ConfigureAwait(false);
        
        return resultCount;       
    }

    public override async Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data) {
        var setClause = string.Join(", ", data.Keys.Select(k => $"{k} = @{k}"));
        var sql = $"UPDATE {table} SET {setClause} WHERE {sfFieldMapping} in @RecordIds";
        
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}, RecordIds: {@RecordIds}", "UPDATE", sql, data, recordIds);
        }
        
        var parameters = new DynamicParameters(data);
        parameters.Add("RecordIds", recordIds.ToArray());
        
        await using var connection = new SqliteConnection(_connectionString);
        var resultCount = await connection.ExecuteAsync(sql, parameters);

        return resultCount;
    }

    public override Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds) {
        var sql = $"DELETE FROM {table} WHERE {sfIdColumnName} in @RecordIds";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordIds: {@RecordIds}", "DELETE", sql, recordIds);
        }

        var parameters = new DynamicParameters();
        parameters.Add("RecordIds", recordIds.ToArray());

        var result = new SqliteConnection(_connectionString)
            .ExecuteAsync(sql, parameters);
        return result;
    }

    public override Task<int> UnDelete(string table, List<string> recordIds) {
        throw new NotImplementedException();
    }

    #region Meta Queries
    /// <summary>
    /// Retrieves complete metadata for a specific table including columns and constraints.
    /// </summary>
    public override async Task<TableMetadata?> GetTableMetadata(string tableName, string schemaName = "public", CancellationToken cancellationToken = default) {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var columns = await GetTableColumns(connection, tableName, cancellationToken).ConfigureAwait(false);
        var constraints = await GetTableConstraints(connection, tableName, cancellationToken).ConfigureAwait(false);

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
    /// Retrieves metadata for all tables in the database.
    /// Note: SQLite doesn't support schemas, so schemaName is ignored.
    /// </summary>
    public override async Task<List<TableMetadata>> GetSchemaMetadata(string schemaName = "public", CancellationToken cancellationToken = default) {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = @"
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
            AND name NOT LIKE 'sqlite_%'
            ORDER BY name";

        var tableNames = await connection.QueryAsync<string>(sql).ConfigureAwait(false);
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
    private async Task<List<ColumnMetadata>> GetTableColumns(SqliteConnection connection, string tableName, CancellationToken cancellationToken = default) {
        const string sql = "PRAGMA table_info({0})";
        var pragmaSql = string.Format(sql, tableName);

        var columns = (await connection.QueryAsync<dynamic>(pragmaSql).ConfigureAwait(false))
            .Select(row => new ColumnMetadata {
                ColumnName = (string)row.name,
                DataType = (string)row.type,
                IsNullable = (long)row.notnull == 0, // notnull == 0 means nullable
                DefaultValue = row.dflt_value != null ? (string)row.dflt_value : null,
                OrdinalPosition = (int)((long)row.cid + 1) // cid is 0-based, convert to 1-based
            })
            .ToList();

        // Enrich each column with its constraints
        foreach (var column in columns) {
            column.ColumnConstraints = await GetColumnConstraints(connection, tableName, column.ColumnName, cancellationToken).ConfigureAwait(false);
        }

        return columns;
    }

    /// <summary>
    /// Retrieves all constraints for a specific table.
    /// </summary>
    private async Task<List<ConstraintMetadata>> GetTableConstraints(SqliteConnection connection, string tableName, CancellationToken cancellationToken = default) {
        const string sql = "PRAGMA foreign_key_list({0})";
        var pragmaSql = string.Format(sql, tableName);

        var fkRows = (await connection.QueryAsync<dynamic>(pragmaSql).ConfigureAwait(false)).ToList();

        var constraints = new List<ConstraintMetadata>();

        // Add foreign key constraints
        foreach (var row in fkRows) {
            var constraintName = $"fk_{tableName}_{row.from}";
            
            constraints.Add(new ConstraintMetadata {
                ConstraintName = constraintName,
                ConstraintType = "FOREIGN KEY",
                Columns = [(string)row.from],
                ReferencedTableName = (string)row.table,
                ReferencedColumns = [(string)row.to]
            });
        }

        return constraints;
    }

    /// <summary>
    /// Retrieves constraints specific to a single column.
    /// </summary>
    private async Task<List<ColumnConstraint>> GetColumnConstraints(SqliteConnection connection, string tableName, string columnName, CancellationToken cancellationToken = default) {
        const string sql = "PRAGMA foreign_key_list({0})";
        var pragmaSql = string.Format(sql, tableName);

        var fkRows = (await connection.QueryAsync<dynamic>(pragmaSql).ConfigureAwait(false))
            .Where(row => (string)row.from == columnName)
            .ToList();

        var constraints = new List<ColumnConstraint>();

        foreach (var row in fkRows) {
            constraints.Add(new ColumnConstraint {
                ConstraintType = "FOREIGN KEY",
                ConstraintName = $"fk_{tableName}_{row.from}",
                ReferencedTable = (string)row.table,
                ReferencedColumn = (string)row.to
            });
        }

        return constraints;
    }

    /// <summary>
    /// Retrieves only the foreign key relationships for a table.
    /// </summary>
    public override async Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string schemaName = "public") {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = "PRAGMA foreign_key_list({0})";
        var pragmaSql = string.Format(sql, tableName);

        var fkRows = (await connection.QueryAsync<dynamic>(pragmaSql).ConfigureAwait(false)).ToList();

        var foreignKeys = new List<ConstraintMetadata>();

        foreach (var row in fkRows) {
            var constraintName = $"fk_{tableName}_{row.from}";
            
            foreignKeys.Add(new ConstraintMetadata {
                ConstraintName = constraintName,
                ConstraintType = "FOREIGN KEY",
                Columns = [(string)row.from],
                ReferencedTableName = (string)row.table,
                ReferencedColumns = [(string)row.to]
            });
        }

        return foreignKeys;
    }
    #endregion
}