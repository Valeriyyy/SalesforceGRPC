using Database.Models;
using Microsoft.Extensions.Logging;

namespace Database.Repositories;

public class MySqlRepository : RepositoryBase {
    public MySqlRepository(ILogger<MySqlRepository> logger, string connectionString, bool debugQuery) : base(logger, connectionString, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.MySql;

    public override Task<int> Create(string table, Dictionary<string, object> data, CancellationToken cancellationToken = default) {
        throw new NotImplementedException();
    }

    public override Task<int> Update(string table, string sfFieldMapping, List<string> recordIds, Dictionary<string, object> data) {
        throw new NotImplementedException();
    }

    public override Task<int> Delete(string table, string sfIdColumnName, List<string> recordIds) {
        throw new NotImplementedException();
    }

    public override Task<int> SoftDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds) {
        throw new NotImplementedException();
    }

    public override Task<int> UnDelete(string table, string sfIdColumnName, string softDeleteColumnName, List<string> recordIds) {
        throw new NotImplementedException();
    }

    public override Task<TableMetadata?> GetTableMetadata(string tableName, string? schemaName = null, CancellationToken cancellationToken = default) {
        throw new NotImplementedException();
    }

    public override Task<List<TableMetadata>> GetSchemaMetadata(string? schemaName = null, CancellationToken cancellationToken = default) {
        throw new NotImplementedException();
    }

    public override Task<List<ConstraintMetadata>> GetForeignKeys(string tableName, string? schemaName = null) {
        throw new NotImplementedException();
    }

    public Task<int> Delete(string table, List<string> recordIds) {
        throw new NotImplementedException();
    }
}