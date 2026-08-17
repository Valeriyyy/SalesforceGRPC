using Application.Services.Interfaces;
using Database.Models;
using Database.Repositories.Interfaces;

namespace Application.Services;

/// <summary>
/// Read-only views of the stored Bindings and their Field Mappings.
/// </summary>
/// <remarks>
/// Writing them belongs to <see cref="IBindingService"/>, which validates before it stores.
/// </remarks>
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
}
