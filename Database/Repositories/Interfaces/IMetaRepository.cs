using Database.Models;

namespace Database.Repositories.Interfaces;
public interface IMetaRepository {
    
    // Task Update(string table, List<string> recordIds, Dictionary<string, object> data, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> GetCachedMapping(int? schemaId, CancellationToken cancellationToken);
    Task<IEnumerable<MappedField>> GetEntityMappedFieldsBySchemaId(int? schemaId);
    Task<List<CDCSchema>> GetCachedSchemas(CancellationToken cancellationToken);
    // Task<IEnumerable<CDCSchema>> GetCDCSchemas(CancellationToken cancellationToken);
    Task<CDCSchema> CreateNewSchema(CDCSchema dbSchema);
}
