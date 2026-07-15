using Database.Models;

namespace Database.Repositories.Interfaces;

public interface IAvroSchemaRepository {
    Task<int> InsertSchemaAsync(DbAvroSchema avroSchema, CancellationToken cancellationToken = default);
    Task<DbAvroSchema?> GetSchemaByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<DbAvroSchema?> GetSchemaBySchemaIdAsync(string schemaId, CancellationToken cancellationToken = default);
    Task<List<DbAvroSchema>> GetSchemaByRecordNameAsync(string recordName, CancellationToken cancellationToken = default);
    Task<List<DbAvroSchema>> GetAllActiveSchemaAsync(CancellationToken cancellationToken = default);
    Task<List<DbAvroSchema>> GetAllSchemasAsync(CancellationToken cancellationToken = default);
    Task<bool> UpdateSchemaAsync(DbAvroSchema avroSchema, CancellationToken cancellationToken = default);
    Task<bool> SetActiveStatusAsync(int schemaId, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> DeleteSchemaAsync(int id, CancellationToken cancellationToken = default);
    Task<int> DeleteSchemasByRecordNameAsync(string recordName, CancellationToken cancellationToken = default);
}