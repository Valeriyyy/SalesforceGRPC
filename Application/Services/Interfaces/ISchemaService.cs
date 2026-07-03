using Database.Models;

namespace Application.Services.Interfaces;

public interface ISchemaService {
    public Task<List<CDCSchema>> GetAllSchemas(CancellationToken cancellationToken = default);
    Task<List<MappedField>> GetMappedFields(int? schemaId);
}