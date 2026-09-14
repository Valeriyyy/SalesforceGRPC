using Dapper;
using Database.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Database.Repositories;

public class SqlServerRepository : RepositoryBase {
    public SqlServerRepository(ILogger<SqlServerRepository> logger, string connectionString, bool debugQuery) : base(logger, connectionString, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.SqlServer;

    /// <summary>SQL Server's own convention for "no schema was specified." Not shared with any other engine.</summary>
    private const string DefaultSchema = "dbo";

    #region Data Queries
    public override async Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "CREATE", sql, data);
        }

        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteAsync(sql, data).ConfigureAwait(false);
    }

    public override async Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data) {
        var setClause = string.Join(", ", data.Keys.Select(k => $"{k} = @{k}"));
        var sql = $"UPDATE {table} SET {setClause} WHERE {sfFieldMapping} IN @RecordIds";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}, RecordIds: {@RecordIds}", "UPDATE", sql, data, recordIds);
        }

        var parameters = new DynamicParameters(data);
        parameters.Add("RecordIds", recordIds);

        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
    }

    public override async Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds) {
        var sql = $"DELETE FROM {table} WHERE {sfIdColumnName} IN @RecordIds";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordIds: {@RecordIds}", "DELETE", sql, recordIds);
        }

        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteAsync(sql, new { RecordIds = recordIds }).ConfigureAwait(false);
    }

    public override Task<int> SoftDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds) =>
        SetSoftDeleteFlag(table, sfIdColumnName, softDeleteColumnName, recordIds, deleted: true);

    public override Task<int> UnDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds) =>
        SetSoftDeleteFlag(table, sfIdColumnName, softDeleteColumnName, recordIds, deleted: false);

    private async Task<int> SetSoftDeleteFlag(string table, string sfIdColumnName, string softDeleteColumnName,
        List<string> recordIds, bool deleted) {
        var sql = $"UPDATE {table} SET {softDeleteColumnName} = @Deleted WHERE {sfIdColumnName} IN @RecordIds";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordIds: {@RecordIds}",
                deleted ? "SOFT DELETE" : "UNDELETE", sql, recordIds);
        }

        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteAsync(sql, new { RecordIds = recordIds, Deleted = deleted }).ConfigureAwait(false);
    }
    #endregion

    #region Meta Queries
    public override async Task<TableMetadata?> GetTableMetadata(string tableName, string? schemaName = null, CancellationToken cancellationToken = default) {
        schemaName ??= DefaultSchema;

        await using var connection = new SqlConnection(_connectionString);
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

    public override async Task<List<TableMetadata>> GetSchemaMetadata(string? schemaName = null, CancellationToken cancellationToken = default) {
        schemaName ??= DefaultSchema;

        await using var connection = new SqlConnection(_connectionString);

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

    private async Task<List<ColumnMetadata>> GetTableColumns(SqlConnection connection, string schemaName, string tableName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT
                column_name as ColumnName,
                data_type as DataType,
                (CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END) as IsNullable,
                column_default as DefaultValue,
                ordinal_position as OrdinalPosition,
                character_maximum_length as MaxLength,
                numeric_precision as NumericPrecision,
                numeric_scale as NumericScale
            FROM information_schema.columns
            WHERE table_schema = @SchemaName
            AND table_name = @TableName
            ORDER BY ordinal_position";

        var columns = (await connection.QueryAsync<ColumnMetadata>(
            sql,
            new { SchemaName = schemaName, TableName = tableName },
            commandTimeout: 30
        ).ConfigureAwait(false)).ToList();

        foreach (var column in columns) {
            column.ColumnConstraints = await GetColumnConstraints(connection, schemaName, tableName, column.ColumnName, cancellationToken).ConfigureAwait(false);
        }

        return columns;
    }

    private async Task<List<ConstraintMetadata>> GetTableConstraints(SqlConnection connection, string schemaName, string tableName, CancellationToken cancellationToken = default) {
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

    private async Task<List<ColumnConstraint>> GetColumnConstraints(SqlConnection connection, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default) {
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

    public override async Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string? schemaName = null) {
        schemaName ??= DefaultSchema;

        await using var connection = new SqlConnection(_connectionString);

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
