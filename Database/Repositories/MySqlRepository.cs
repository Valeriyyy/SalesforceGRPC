using Dapper;
using Database.Models;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Database.Repositories;

public class MySqlRepository : RepositoryBase {
    /// <summary>
    /// MySQL has no schema separate from its database, so "no schema was specified" resolves to the database
    /// this connection already points at, parsed once from the connection string — not a shared literal like
    /// Postgres's "public", since it's specific to this one connection.
    /// </summary>
    private readonly string _defaultSchema;

    public MySqlRepository(ILogger<MySqlRepository> logger, string connectionString, bool debugQuery) : base(logger, connectionString, debugQuery) {
        _defaultSchema = new MySqlConnectionStringBuilder(connectionString).Database;
    }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.MySql;

    #region Data Queries
    public override async Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        var columns = string.Join(", ", data.Keys);
        var parameters = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {table} ({columns}) VALUES ({parameters})";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, Values: {@Values}", "CREATE", sql, data);
        }

        await using var connection = new MySqlConnection(_connectionString);
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

        await using var connection = new MySqlConnection(_connectionString);
        return await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
    }

    public override async Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds) {
        var sql = $"DELETE FROM {table} WHERE {sfIdColumnName} IN @RecordIds";
        if (_debugQuery) {
            _logger.LogInformation("QueryType: {QueryType}, SQL: {SQL}, RecordIds: {@RecordIds}", "DELETE", sql, recordIds);
        }

        await using var connection = new MySqlConnection(_connectionString);
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

        await using var connection = new MySqlConnection(_connectionString);
        return await connection.ExecuteAsync(sql, new { RecordIds = recordIds, Deleted = deleted }).ConfigureAwait(false);
    }
    #endregion

    #region Meta Queries
    public override async Task<TableMetadata?> GetTableMetadata(string tableName, string? schemaName = null, CancellationToken cancellationToken = default) {
        schemaName ??= _defaultSchema;

        await using var connection = new MySqlConnection(_connectionString);
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
        schemaName ??= _defaultSchema;

        await using var connection = new MySqlConnection(_connectionString);

        const string sql = @"
            SELECT TABLE_NAME as TableName
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @SchemaName
            AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME";

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

    private async Task<List<ColumnMetadata>> GetTableColumns(MySqlConnection connection, string schemaName, string tableName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT
                COLUMN_NAME as ColumnName,
                DATA_TYPE as DataType,
                (IS_NULLABLE = 'YES') as IsNullable,
                COLUMN_DEFAULT as DefaultValue,
                ORDINAL_POSITION as OrdinalPosition,
                CHARACTER_MAXIMUM_LENGTH as MaxLength,
                NUMERIC_PRECISION as NumericPrecision,
                NUMERIC_SCALE as NumericScale
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @SchemaName
            AND TABLE_NAME = @TableName
            ORDER BY ORDINAL_POSITION";

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

    /// <summary>
    /// Unlike Postgres/SQL Server, MySQL's key_column_usage already carries REFERENCED_TABLE_NAME and
    /// REFERENCED_COLUMN_NAME directly for foreign-key rows — there is no constraint_column_usage view to
    /// join against, because MySQL has nothing else that information could live in.
    /// </summary>
    private async Task<List<ConstraintMetadata>> GetTableConstraints(MySqlConnection connection, string schemaName, string tableName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT
                tc.CONSTRAINT_NAME as ConstraintName,
                tc.CONSTRAINT_TYPE as ConstraintType,
                GROUP_CONCAT(DISTINCT kcu.COLUMN_NAME ORDER BY kcu.ORDINAL_POSITION SEPARATOR ', ') as ColumnList,
                MAX(kcu.REFERENCED_TABLE_NAME) as ReferencedTableName,
                MAX(kcu.REFERENCED_COLUMN_NAME) as ReferencedColumnName
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                AND tc.TABLE_NAME = kcu.TABLE_NAME
            WHERE tc.TABLE_SCHEMA = @SchemaName
            AND tc.TABLE_NAME = @TableName
            GROUP BY tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE
            ORDER BY tc.CONSTRAINT_TYPE, tc.CONSTRAINT_NAME";

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

    private async Task<List<ColumnConstraint>> GetColumnConstraints(MySqlConnection connection, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default) {
        const string sql = @"
            SELECT
                tc.CONSTRAINT_TYPE as ConstraintType,
                tc.CONSTRAINT_NAME as ConstraintName,
                kcu.REFERENCED_TABLE_NAME as ReferencedTable,
                kcu.REFERENCED_COLUMN_NAME as ReferencedColumn
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                AND tc.TABLE_NAME = kcu.TABLE_NAME
            WHERE tc.TABLE_SCHEMA = @SchemaName
            AND tc.TABLE_NAME = @TableName
            AND kcu.COLUMN_NAME = @ColumnName
            ORDER BY tc.CONSTRAINT_TYPE";

        return (await connection.QueryAsync<ColumnConstraint>(
            sql,
            new { SchemaName = schemaName, TableName = tableName, ColumnName = columnName },
            commandTimeout: 30
        ).ConfigureAwait(false)).ToList();
    }

    public override async Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string? schemaName = null) {
        schemaName ??= _defaultSchema;

        await using var connection = new MySqlConnection(_connectionString);

        const string sql = @"
            SELECT
                tc.CONSTRAINT_NAME as ConstraintName,
                'FOREIGN KEY' as ConstraintType,
                GROUP_CONCAT(DISTINCT kcu.COLUMN_NAME ORDER BY kcu.ORDINAL_POSITION SEPARATOR ', ') as ColumnList,
                MAX(kcu.REFERENCED_TABLE_NAME) as ReferencedTableName,
                GROUP_CONCAT(DISTINCT kcu.REFERENCED_COLUMN_NAME ORDER BY kcu.ORDINAL_POSITION SEPARATOR ', ') as ReferencedColumnList
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                AND tc.TABLE_NAME = kcu.TABLE_NAME
            WHERE tc.TABLE_SCHEMA = @SchemaName
            AND tc.TABLE_NAME = @TableName
            AND tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
            GROUP BY tc.CONSTRAINT_NAME
            ORDER BY tc.CONSTRAINT_NAME";

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
