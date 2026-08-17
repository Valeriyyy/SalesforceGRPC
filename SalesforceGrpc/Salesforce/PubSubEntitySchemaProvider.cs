using Application.Bindings;
using Avro;
using com.sforce.eventbus;
using Database.Models;
using Database.Repositories.Interfaces;
using Grpc.Core;
using GrpcClient;
using Microsoft.Extensions.Caching.Memory;

namespace SalesforceGrpc.Salesforce;

/// <summary>
/// Supplies an Entity's Avro Schema, asking Salesforce for the current revision and caching it locally.
/// </summary>
/// <remarks>
/// Salesforce issues a new Schema Id every time the Source Object's shape changes, so the current revision is
/// read from Pub/Sub rather than assumed from what the App Database already holds. The result is cached
/// briefly because the binding UI asks for it on every page view, and falls back to the newest stored copy
/// when Salesforce cannot be reached — a user configuring a mapping should not be blocked by a transient
/// outage.
/// </remarks>
public sealed class PubSubEntitySchemaProvider : IEntitySchemaProvider {

    /// <summary>Long enough to keep the UI off the wire, short enough to notice a schema revision promptly.</summary>
    private static readonly TimeSpan LookupCacheDuration = TimeSpan.FromMinutes(5);

    private const string CacheKeyPrefix = "entity_schema_";

    private readonly PubSub.PubSubClient _pubsubClient;
    private readonly IAvroSchemaRepository _avroSchemas;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PubSubEntitySchemaProvider> _logger;

    public PubSubEntitySchemaProvider(PubSub.PubSubClient pubsubClient, IAvroSchemaRepository avroSchemas,
        IMemoryCache cache, ILogger<PubSubEntitySchemaProvider> logger) {
        _pubsubClient = pubsubClient;
        _avroSchemas = avroSchemas;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DbAvroSchema?> GetSchemaForEntityAsync(string entityName, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var cacheKey = $"{CacheKeyPrefix}{entityName}";
        if (_cache.TryGetValue(cacheKey, out DbAvroSchema? cached) && cached is not null) {
            return cached;
        }

        var schema = await FetchCurrentSchema(entityName, cancellationToken).ConfigureAwait(false)
                     ?? await MostRecentStoredSchema(entityName, cancellationToken).ConfigureAwait(false);

        if (schema is not null) {
            _cache.Set(cacheKey, schema, LookupCacheDuration);
        }

        return schema;
    }

    private async Task<DbAvroSchema?> FetchCurrentSchema(string entityName, CancellationToken cancellationToken) {
        try {
            var topic = await _pubsubClient.GetTopicAsync(
                new TopicRequest { TopicName = $"/data/{entityName}" }, cancellationToken: cancellationToken);

            var stored = await _avroSchemas.GetSchemaBySchemaIdAsync(topic.SchemaId, cancellationToken).ConfigureAwait(false);
            if (stored is not null) {
                return stored;
            }

            var fetched = await _pubsubClient.GetSchemaAsync(
                new SchemaRequest { SchemaId = topic.SchemaId }, cancellationToken: cancellationToken);

            var record = Schema.Parse(fetched.SchemaJson);

            var avroSchema = new DbAvroSchema {
                SchemaId = fetched.SchemaId,
                RecordName = record.Name,
                SchemaJson = fetched.SchemaJson,
                DateCreated = DateTime.UtcNow
            };

            avroSchema.Id = await _avroSchemas.InsertSchemaAsync(avroSchema, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Stored Avro Schema {SchemaId} for {Entity}", avroSchema.SchemaId, entityName);
            return avroSchema;
        } catch (RpcException ex) {
            _logger.LogWarning(ex, "Could not read the current Avro Schema for {Entity} from Salesforce; falling back to the stored copy", entityName);
            return null;
        }
    }

    private async Task<DbAvroSchema?> MostRecentStoredSchema(string entityName, CancellationToken cancellationToken) {
        var stored = await _avroSchemas.GetSchemaByRecordNameAsync(entityName, cancellationToken).ConfigureAwait(false);
        return stored.OrderByDescending(s => s.DateCreated).FirstOrDefault();
    }
}
