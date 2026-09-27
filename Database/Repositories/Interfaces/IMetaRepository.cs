using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// Reads and writes Bindings, Field Mappings and their caches in the App Database.
/// </summary>
/// <remarks>
/// Always Postgres — only the Target Database is pluggable. Every write invalidates the caches it affects, so
/// a Binding changed through the API reaches the running worker without waiting out the sliding expiration.
/// </remarks>
public interface IMetaRepository {

    Task<Dictionary<string, string>> GetCachedMapping(int? schemaId, CancellationToken cancellationToken);
    Task<IEnumerable<MappedField>> GetEntityMappedFieldsBySchemaId(int? schemaId);
    Task<List<CDCSchema>> GetCachedSchemas(CancellationToken cancellationToken = default);
    Task<CDCSchema> CreateNewSchema(CDCSchema dbSchema);
    Task<CDCSchema?> GetSchemaById(int schemaId);
    Task<CDCSchema?> GetSchemaByRecordName(string recordName);
    Task<CDCSchema> CreateNewSchemaWithAvroLink(CDCSchema dbSchema, int avroSchemaId);
    Task<bool> UpdateCdcSchemaWithAvroLink(int cdcSchemaId, int avroSchemaId);

    #region Bindings

    /// <summary>Finds the Binding for an Entity, or null when it has none.</summary>
    Task<CDCSchema?> GetSchemaByEntityName(string entityName);

    /// <summary>Finds the Binding writing to a Target Table, or null when nothing writes to it.</summary>
    Task<CDCSchema?> GetSchemaByTargetTable(string dbSchemaFullName);

    /// <summary>
    /// Updates the Target Table and soft delete settings of an existing Binding. Does not change its state.
    /// </summary>
    Task<bool> UpdateBinding(int bindingId, string dbSchemaFullName, bool softDeleteEnabled,
        string? softDeleteColumnName);

    /// <summary>
    /// Moves a Binding to a new <see cref="BindingState"/>, clearing any record that the worker forced it to
    /// Incomplete.
    /// </summary>
    Task<bool> SetBindingState(int bindingId, BindingState state);

    /// <summary>
    /// Sets an Active Binding back to Incomplete on the worker's behalf, recording when, so it can be told apart
    /// from a Binding that was never finished.
    /// </summary>
    Task<bool> ForceBindingIncomplete(int bindingId, DateTime forcedAt);

    /// <summary>How many Bindings are in each state, read fresh rather than from the schema cache.</summary>
    Task<BindingStateCounts> CountBindingsByStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes a Binding and, by cascade of the write below, its Field Mappings.</summary>
    Task<bool> DeleteBinding(int bindingId);

    #endregion

    #region Field Mappings

    /// <summary>
    /// Replaces a Binding's entire Field Mapping set in one transaction.
    /// </summary>
    /// <remarks>
    /// Replace rather than merge: the caller always holds the full intended set, and a partial write would
    /// leave a Binding whose stored mappings match neither what the user saw nor what they submitted.
    /// </remarks>
    Task ReplaceFieldMappings(int bindingId, IEnumerable<MappedField> mappings);

    #endregion
}

/// <summary>How many Bindings are in each <see cref="BindingState"/>.</summary>
public sealed record BindingStateCounts {
    public int Active { get; init; }

    /// <summary>Every Incomplete Binding, including those counted in <see cref="ForcedIncomplete"/>.</summary>
    public int Incomplete { get; init; }

    public int Inactive { get; init; }

    /// <summary>Incomplete Bindings the worker set back from Active because their Key Mapping lost its unique constraint.</summary>
    public int ForcedIncomplete { get; init; }
}
