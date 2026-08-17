using Database.Models;

namespace Application.Bindings;

/// <summary>
/// Supplies the Avro Schema for an Entity, fetching it from Salesforce when the App Database has none.
/// </summary>
/// <remarks>
/// An interface rather than a direct dependency because fetching means the Pub/Sub gRPC client, which lives in
/// the host project alongside the generated protobuf types. Keeping it behind this seam is also what lets a
/// second source of field detail — the REST describe — be layered in later without disturbing callers.
/// </remarks>
public interface IEntitySchemaProvider {
    /// <summary>
    /// Returns the current Avro Schema for an Entity, or null when Salesforce does not publish one.
    /// </summary>
    /// <param name="entityName">The Entity name, e.g. "AccountChangeEvent".</param>
    Task<DbAvroSchema?> GetSchemaForEntityAsync(string entityName, CancellationToken cancellationToken = default);
}
