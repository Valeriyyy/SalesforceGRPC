using Database.Models;

namespace Application.Services.Interfaces;

public interface IFieldMappingService {
    Task<TableMetadata> GetTargetDatabaseTableMetadata(string dbTableName, string schemaName, CancellationToken cancellationToken = default);
}