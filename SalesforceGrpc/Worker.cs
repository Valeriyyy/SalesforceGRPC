using Application.Bindings;
using Application.Services.Interfaces;
using Avro;
using Avro.Generic;
using Avro.IO;
using com.sforce.eventbus;
using Common;
using Database.Models;
using Database.Repositories.Interfaces;
using Grpc.Core;
using GrpcClient;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Strategies;

namespace SalesforceGrpc;

/// <summary>
/// Streams change events for the Primary Channel and applies the Active Bindings to the target database.
/// </summary>
/// <remarks>
/// What to subscribe to and which Bindings to apply is decided by <see cref="IBindingService"/>, not here, so
/// that decision is unit testable. This class owns only the streaming loop.
/// </remarks>
public class Worker : BackgroundService {
    private readonly ILogger<Worker> _logger;
    private readonly PubSub.PubSubClient _pubsubClient;
    private readonly EventResolver _eventResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBindingChangeSignal _changeSignal;

    private readonly IMetaRepository _metaRepo;
    private readonly IAvroSchemaRepository _avroSchemaRepo;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    private const int EventsPerFetch = 25;

    public Worker(
        ILogger<Worker> logger,
        PubSub.PubSubClient psClient,
        IHostApplicationLifetime hostApplicationLifetime,
        IMetaRepository metaRepo,
        IAvroSchemaRepository avroSchemaRepo,
        IServiceScopeFactory scopeFactory,
        IBindingChangeSignal changeSignal,
        EventResolver eventResolver) {
        _logger = logger;
        _pubsubClient = psClient;
        _hostApplicationLifetime = hostApplicationLifetime;
        _metaRepo = metaRepo;
        _avroSchemaRepo = avroSchemaRepo;
        _scopeFactory = scopeFactory;
        _changeSignal = changeSignal;
        _eventResolver = eventResolver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                var plan = await GetPlan(stoppingToken).ConfigureAwait(false);

                if (!plan.HasChannel) {
                    // A fresh install has no Primary Channel. Idling beats refusing to boot.
                    _logger.LogWarning(
                        "No Primary Channel is configured, so there is nothing to subscribe to. Select one through the API and the worker will start streaming without a restart.");
                    await _changeSignal.WaitForChangeAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (plan.ActiveBindingsBySchemaId.Count == 0) {
                    _logger.LogWarning(
                        "Primary Channel {Channel} has no Active Bindings. Events will be received and skipped until a Binding is activated.",
                        plan.ChannelFullName);
                }

                await ListenForChannelEvents(plan, stoppingToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Worker stopping");
        } catch (RpcException exc) {
            // There is no replay-ID checkpointing yet, so a dropped stream cannot be resumed without gaps.
            // Shutting down is honest about that; silently reconnecting would lose events invisibly.
            _logger.LogCritical(exc, "RPCException thrown with message: {message}", exc.Message);
            _logger.LogCritical("Status: {status}", exc.StatusCode);
            _logger.LogCritical("Shutting down application gracefully");
            _hostApplicationLifetime.StopApplication();
        }
    }

    private async Task<SubscriptionPlan> GetPlan(CancellationToken cancellationToken) {
        // The service is scoped; the worker is not, so a scope per re-plan rather than a captured instance.
        using var scope = _scopeFactory.CreateScope();
        var bindings = scope.ServiceProvider.GetRequiredService<IBindingService>();
        return await bindings.GetSubscriptionPlanAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ListenForChannelEvents(SubscriptionPlan plan, CancellationToken stoppingToken) {
        // A configuration change ends the stream so the loop re-plans against the new Bindings, rather than
        // running on stale routing until the caches expire.
        using var planScope = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var planToken = planScope.Token;
        var watcher = WatchForConfigurationChange(planScope);

        var bindings = new Dictionary<string, CDCSchema>(plan.ActiveBindingsBySchemaId, StringComparer.Ordinal);

        try {
            var fetchRequest = new FetchRequest {
                TopicName = plan.TopicName,
                NumRequested = EventsPerFetch
            };

            _logger.LogInformation("Subscribing to {Topic} with {Count} active binding(s)",
                plan.TopicName, bindings.Count);

            using var stream = _pubsubClient.Subscribe(null, null, planToken);
            await stream.RequestStream.WriteAsync(fetchRequest, planToken).ConfigureAwait(false);

            while (await stream.ResponseStream.MoveNext(planToken).ConfigureAwait(false)) {
                var response = stream.ResponseStream.Current;
                _logger.LogInformation("Latest Replay Id: {replayId}, RPC Id: {RpcId}",
                    response.LatestReplayId.ToLongBE(), response.RpcId);

                if (response.Events is null || response.Events.Count == 0) {
                    continue;
                }

                var eventTasks = response.Events
                    .Select(e => ApplyEvent(e, bindings, planToken))
                    .ToList();

                await Task.WhenAll(eventTasks).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Configuration changed; rebuilding the subscription plan");
        } catch (RpcException exc) when (exc.StatusCode == StatusCode.Cancelled && !stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Configuration changed; rebuilding the subscription plan");
        } finally {
            if (!planScope.IsCancellationRequested) {
                await planScope.CancelAsync().ConfigureAwait(false);
            }
            await watcher.ConfigureAwait(false);
        }
    }

    private Task WatchForConfigurationChange(CancellationTokenSource planScope) {
        return Task.Run(async () => {
            try {
                await _changeSignal.WaitForChangeAsync(planScope.Token).ConfigureAwait(false);
                await planScope.CancelAsync().ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // The stream ended first; nothing to do.
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Applies one event, isolating its failure so one bad record cannot stop the batch or the stream.
    /// </summary>
    private async Task ApplyEvent(ConsumerEvent consumerEvent,
        Dictionary<string, CDCSchema> bindings, CancellationToken cancellationToken) {
        try {
            _logger.LogInformation("Event Replay Id: {replayId}, Schema Id: {schemaId}",
                consumerEvent.ReplayId.ToLongBE(), consumerEvent.Event.SchemaId);

            var binding = await ResolveBinding(consumerEvent.Event.SchemaId, bindings, cancellationToken)
                .ConfigureAwait(false);

            if (binding is null) {
                return;
            }

            if (binding.AvroSchema?.SchemaJson is not { } schemaJson) {
                _logger.LogError("Binding {BindingId} has no Avro Schema to decode {Entity} with",
                    binding.Id, binding.EntityName);
                return;
            }

            var schema = Schema.Parse(schemaJson);

            using var memStream = new MemoryStream(consumerEvent.Event.Payload.ToByteArray());
            var decoder = new BinaryDecoder(memStream);
            var datumReader = new GenericDatumReader<GenericRecord>(schema, schema);
            var record = datumReader.Read(null!, decoder);

            if (!record.GetTypedValue<GenericRecord>("ChangeEventHeader", out var changeEventHeader) ||
                !changeEventHeader.GetTypedValue<GenericEnum>("changeType", out var changeType)) {
                _logger.LogWarning("Event for {Entity} carries no readable ChangeEventHeader", binding.EntityName);
                return;
            }

            if (!Enum.TryParse(changeType.Value, out ChangeType changeTypeEnum)) {
                _logger.LogWarning("Unrecognised change type '{ChangeType}' for {Entity}", changeType.Value, binding.EntityName);
                return;
            }

            _logger.LogInformation("Processing {ChangeType} for {Entity}", changeTypeEnum, binding.EntityName);

            var strategy = _eventResolver.Resolve(changeTypeEnum);
            await strategy.ProcessEvent(record, schema, binding, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to apply event with Schema Id {SchemaId}; continuing with the rest of the batch",
                consumerEvent.Event.SchemaId);
        }
    }

    /// <summary>
    /// Finds the Active Binding for an incoming event, or null when the event should be skipped.
    /// </summary>
    /// <remarks>
    /// An event carries only an Avro Schema Id. An unrecognised one usually means Salesforce revised the
    /// entity's shape and issued a new Id, so the schema is fetched and the existing Binding relinked to it.
    /// A Binding is never created here — the destination for an entity is the user's decision, and the old
    /// behaviour of inventing "salesforce.&lt;entity&gt;" wrote data somewhere nobody chose.
    /// </remarks>
    private async Task<CDCSchema?> ResolveBinding(string schemaId,
        Dictionary<string, CDCSchema> bindings, CancellationToken cancellationToken) {
        lock (bindings) {
            if (bindings.TryGetValue(schemaId, out var known)) {
                return known;
            }
        }

        _logger.LogInformation("Unrecognised Schema Id {SchemaId}; fetching it from Salesforce", schemaId);

        var avroSchema = await FetchAndStoreSchema(schemaId, cancellationToken).ConfigureAwait(false);
        if (avroSchema is null) {
            return null;
        }

        var binding = await _metaRepo.GetSchemaByEntityName(avroSchema.RecordName).ConfigureAwait(false);

        if (binding is null) {
            _logger.LogDebug("No Binding for {Entity}; skipping the event. Create one to start syncing it.",
                avroSchema.RecordName);
            return null;
        }

        // Relink the Binding to the revision that just arrived so later events decode against the right shape.
        if (binding.AvroSchemaId != avroSchema.Id) {
            await _metaRepo.UpdateCdcSchemaWithAvroLink(binding.Id, avroSchema.Id).ConfigureAwait(false);
            binding.AvroSchemaId = avroSchema.Id;
        }
        binding.AvroSchema = avroSchema;

        if (!binding.IsActive) {
            _logger.LogDebug("Binding {BindingId} for {Entity} is {State}; skipping the event.",
                binding.Id, binding.EntityName, binding.BindingState);
            return null;
        }

        lock (bindings) {
            // Drop the entry for the superseded revision so the dictionary does not grow with every change.
            var stale = bindings.FirstOrDefault(kv => kv.Value.EntityName == binding.EntityName).Key;
            if (stale is not null) {
                bindings.Remove(stale);
            }
            bindings[schemaId] = binding;
        }

        return binding;
    }

    private async Task<DbAvroSchema?> FetchAndStoreSchema(string schemaId, CancellationToken cancellationToken) {
        try {
            var existing = await _avroSchemaRepo.GetSchemaBySchemaIdAsync(schemaId, cancellationToken).ConfigureAwait(false);
            if (existing is not null) {
                return existing;
            }

            var schemaInfo = await _pubsubClient.GetSchemaAsync(
                new SchemaRequest { SchemaId = schemaId }, cancellationToken: cancellationToken);

            var parsed = Schema.Parse(schemaInfo.SchemaJson);

            var avroSchema = new DbAvroSchema {
                SchemaId = schemaInfo.SchemaId,
                RecordName = parsed.Name,
                SchemaJson = schemaInfo.SchemaJson,
                DateCreated = DateTime.UtcNow
            };

            avroSchema.Id = await _avroSchemaRepo.InsertSchemaAsync(avroSchema, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Stored Avro Schema {SchemaId} for {RecordName}", avroSchema.SchemaId, avroSchema.RecordName);

            return avroSchema;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Error retrieving Avro Schema {SchemaId}", schemaId);
            return null;
        }
    }
}
