using Application.Services.Interfaces;
using Database.Models;
using Database.Repositories.Interfaces;
using DTO;

namespace Application.Services;

public class SchemaService : ISchemaService {
    private readonly IMetaRepository _db;

    public SchemaService(IMetaRepository db) {
        _db = db;
    }

    public async Task<List<CDCSchema>> GetAllSchemas(CancellationToken cancellationToken = default) {
        var schemas = await _db.GetCachedSchemas(cancellationToken).ConfigureAwait(false);
        return schemas;
    }
    
    public async Task<List<MappedField>> GetMappedFields(int? schemaId) {
        var mappedFields = await _db.GetEntityMappedFieldsBySchemaId(schemaId).ConfigureAwait(false);
        return mappedFields.ToList();
    }

    public async Task CreateFieldMapping(CreateFieldMappingDTO fieldMappingsToCreate) {
        // first check if the schema exists
        var schema = await _db.GetSchemaById(fieldMappingsToCreate.SchemaId).ConfigureAwait(false);
        
        if(schema == null) {
            throw new Exception($"Schema with ID {fieldMappingsToCreate.SchemaId} does not exist.");
        }
        
        // check if the salesforce fields are already mapped to the database fields
        // check if the salesforce fields belong in the schema
        
        
    }
}