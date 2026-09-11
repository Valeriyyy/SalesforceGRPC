using Application.Bindings;
using Application.Services.Interfaces;
using Application.Targets;
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
    private readonly IConfigurationChangeSignal _changeSignal;

    private readonly IMetaRepository _metaRepo;
    private readonly IAvroSchemaRepository _avroSchemaRepo;

    private const int EventsPerFetch = 25;

    public Worker(
        ILogger<Worker> logger,
        PubSub.PubSubClient psClient,
        IMetaRepository metaRepo,
        IAvroSchemaRepository avroSchemaRepo,
        IServiceScopeFactory scopeFactory,
        IConfigurationChangeSignal changeSignal,
        EventResolver eventResolver) {
        _logger = logger;
        _pubsubClient = psClient;
        _metaRepo = metaRepo;
        _avroSchemaRepo = avroSchemaRepo;
        _scopeFactory = scopeFactory;
        _changeSignal = changeSignal;
        _eventResolver = eventResolver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                SubscriptionPlan plan;
                try {
                    plan = await GetPlan(stoppingToken).ConfigureAwait(false);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    // Re-planning reads the App Database, so an unmigrated schema or a database that is down
                    // lands here. Letting it escape would end the supervisor loop for good — and under the
                    // host's default BackgroundService behaviour, take the process with it — which is exactly
                    // the shutdown-on-failure this loop exists to stop doing.
                    _logger.LogError(ex,
                        "Could not build a subscription plan; retrying in {Delay}s. The API stays available.",
                        RetryDelay.TotalSeconds);
                    await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!plan.HasConnection) {
                    // A fresh install has no Org Connection, and a broken one is repaired through the API
                    // this host serves. Idling beats refusing to boot, and beats shutting down.
                    _logger.LogWarning(
                        "No usable Salesforce Org Connection, so there is nothing to stream. Set one up through the API and the worker will start without a restart.");
                    await _changeSignal.WaitForChangeAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!plan.HasTargetDatabase) {
                    // Three situations, treated two ways. Absent and Incomplete wait for the user, because
                    // nothing will change without them. Failed retries on its own, because a database that
                    // went away usually comes back — and the retry is a proof, so the recovery is recorded.
                    switch (plan.TargetConnectionState) {
                        case ConnectionState.Failed:
                            _logger.LogError(
                                "The Target Connection is Failed, so events are not being consumed. Retrying in {Delay}s.",
                                RetryDelay.TotalSeconds);
                            await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                            await RetestTargetAsync(stoppingToken).ConfigureAwait(false);
                            continue;
                        case ConnectionState.Incomplete:
                            _logger.LogWarning(
                                "The Target Connection has never been proved, so there is nowhere to write events. Correct it through the API and the worker will start without a restart.");
                            break;
                        default:
                            _logger.LogWarning(
                                "No Target Connection is configured, so there is nowhere to write events. Set one up through the API and the worker will start without a restart.");
                            break;
                    }
                    await _changeSignal.WaitForChangeAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

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
        }
    }

    /// <summary>
    /// Waits before re-planning after a failure, so a persistent fault does not become a hot loop.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Reports a dropped stream and pauses before reconnecting.
    /// </summary>
    /// <remarks>
    /// This used to call <c>StopApplication()</c>, with a deliberate justification: there is no replay-ID
    /// checkpointing, so a dropped stream cannot be resumed without gaps, and shutting down was honest about
    /// that where silently reconnecting would lose events invisibly.
    /// <para>
    /// That reasoning is sound and is overruled knowingly. Credentials are now managed through the API this
    /// host serves, and a service that kills itself when a credential fails cannot be repaired through the UI
    /// that manages credentials. The cost is real and unchanged: every event between the drop and the
    /// reconnect is lost, with no record of how many. The remedy is replay-ID checkpointing. Until it exists,
    /// this logs loudly enough that the gap is at least visible.
    /// </para>
    /// </remarks>
    private async Task ReportDropAndPause(RpcException exc, CancellationToken stoppingToken) {
        _logger.LogCritical(exc,
            "The Salesforce event stream dropped ({Status}): {Message}. Reconnecting in {Delay}s — " +
            "events published during the gap will NOT be replayed, and there is no record of how many. " +
            "Replay-ID checkpointing is the remedy and is not implemented yet.",
            exc.StatusCode, exc.Message, RetryDelay.TotalSeconds);

        await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the write failure on the Target Connection and ends the stream.
    /// </summary>
    /// <remarks>
    /// Dropping rather than holding the stream open and logging per event. Holding it open consumes every
    /// event in the outage window into a database that cannot store them, with no record of how many, while
    /// hammering that database. Dropping loses the same events — there is still no replay-ID checkpointing —
    /// but stops the hammering and makes the health check tell the truth. The plan loop then retries.
    /// </remarks>
    private async Task ReportTargetFailureAndDrop(TargetDatabaseWriteException exc, CancellationToken stoppingToken) {
        _logger.LogCritical(exc,
            "The Target Database refused a write, so the stream has been dropped: {Message}. Events published until it " +
            "recovers will NOT be replayed, and there is no record of how many. Retrying in {Delay}s.",
            exc.Message, RetryDelay.TotalSeconds);

        try {
            using var scope = _scopeFactory.CreateScope();
            var targets = scope.ServiceProvider.GetRequiredService<ITargetConnectionService>();
            await targets.RecordWriteFailureAsync(exc.InnerException ?? exc, stoppingToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Could not record the Target Connection failure; the state may still read Connected");
        }
    }

    /// <summary>Proves the Failed Target Connection again. Success is recorded, and logged loudly, by the service.</summary>
    private async Task RetestTargetAsync(CancellationToken stoppingToken) {
        try {
            using var scope = _scopeFactory.CreateScope();
            var targets = scope.ServiceProvider.GetRequiredService<ITargetConnectionService>();
            await targets.RetestAsync(stoppingToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Could not re-test the Target Connection; will retry");
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
        } catch (RpcException exc) {
            await ReportDropAndPause(exc, stoppingToken).ConfigureAwait(false);
        } catch (TargetDatabaseWriteException exc) {
            await ReportTargetFailureAndDrop(exc, stoppingToken).ConfigureAwait(false);
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
        } catch (TargetDatabaseWriteException) {
            // The database, not this event. Escapes the batch so the stream is dropped.
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
