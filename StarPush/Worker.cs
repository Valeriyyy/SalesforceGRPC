using Application.Bindings;
using Application.Runtime;
using Application.Services.Interfaces;
using Application.Targets;
using Avro;
using Avro.Generic;
using Avro.IO;
using com.sforce.eventbus;
using Common;
using Database.Models;
using Database.Repositories.Interfaces;
using Google.Protobuf;
using Grpc.Core;
using GrpcClient;
using StarPush.Extensions;
using StarPush.Strategies;

namespace StarPush;

/// <summary>
/// Streams change events for the Primary Channel and applies the Active Bindings to the target database.
/// </summary>
/// <remarks>
/// What to subscribe to, where to start and which Bindings to apply is decided by <see cref="IBindingService"/>,
/// not here. This class owns the streaming loop: flow control, applying each batch in record order, and saving
/// the Channel's Checkpoint once a batch is done, so any interruption resumes where it left off.
/// <para>
/// Delivery is at-least-once (ADR 0005). A batch interrupted part-way is replayed from its start, so every write
/// the strategies make must be safe to repeat. Only one worker per App Database is assumed: two would
/// overwrite each other's Checkpoint.
/// </para>
/// </remarks>
public class Worker : BackgroundService {
    private readonly ILogger<Worker> _logger;
    private readonly PubSub.PubSubClient _pubsubClient;
    private readonly EventResolver _eventResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfigurationChangeSignal _changeSignal;

    private readonly IMetaRepository _metaRepo;
    private readonly IAvroSchemaRepository _avroSchemaRepo;
    private readonly ICheckpointRepository _checkpoints;
    private readonly InboundHealth _health;
    private readonly IIncidentRecorder _incidents;
    private readonly IInboundFailureRepository _failures;
    private readonly TimeProvider _time;

    private const int EventsPerFetch = 25;

    public Worker(
        ILogger<Worker> logger,
        PubSub.PubSubClient psClient,
        IMetaRepository metaRepo,
        IAvroSchemaRepository avroSchemaRepo,
        ICheckpointRepository checkpoints,
        IServiceScopeFactory scopeFactory,
        IConfigurationChangeSignal changeSignal,
        EventResolver eventResolver,
        InboundHealth health,
        IIncidentRecorder incidents,
        IInboundFailureRepository failures,
        TimeProvider time) {
        _logger = logger;
        _pubsubClient = psClient;
        _metaRepo = metaRepo;
        _avroSchemaRepo = avroSchemaRepo;
        _checkpoints = checkpoints;
        _scopeFactory = scopeFactory;
        _changeSignal = changeSignal;
        _eventResolver = eventResolver;
        _health = health;
        _incidents = incidents;
        _failures = failures;
        _time = time;
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
                    await ReportRetryingAsync($"Could not work out what to stream: {ex.Message}").ConfigureAwait(false);
                    await Task.Delay(RetryDelay, _time, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!plan.HasConnection) {
                    // A fresh install has no Org Connection, and a broken one is repaired through the API
                    // this host serves. Idling beats refusing to boot, and beats shutting down.
                    _logger.LogWarning(
                        "No usable Salesforce Org Connection, so there is nothing to stream. Set one up through the API and the worker will start without a restart.");
                    await ReportAsync(RunState.Blocked,
                        "There is no usable Org Connection. Set it up or verify it, and syncing starts by itself.").ConfigureAwait(false);
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
                            await ReportRetryingAsync("The Target Connection is Failed, so nothing can be written. Checking it again.")
                                .ConfigureAwait(false);
                            await Task.Delay(RetryDelay, _time, stoppingToken).ConfigureAwait(false);
                            await VerifyTargetAsync(stoppingToken).ConfigureAwait(false);
                            continue;
                        case ConnectionState.Incomplete:
                            _logger.LogWarning(
                                "The Target Connection has never been proved, so there is nowhere to write events. Correct it through the API and the worker will start without a restart.");
                            await ReportAsync(RunState.Blocked,
                                "The Target Connection has never been proved, so there is nowhere to write. Correct it, and syncing starts by itself.")
                                .ConfigureAwait(false);
                            break;
                        default:
                            _logger.LogWarning(
                                "No Target Connection is configured, so there is nowhere to write events. Set one up through the API and the worker will start without a restart.");
                            await ReportAsync(RunState.Blocked,
                                "There is no Target Connection, so there is nowhere to write. Set one up, and syncing starts by itself.")
                                .ConfigureAwait(false);
                            break;
                    }
                    await _changeSignal.WaitForChangeAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!plan.HasChannel) {
                    // A fresh install has no Primary Channel. Idling beats refusing to boot.
                    _logger.LogWarning(
                        "No Primary Channel is configured, so there is nothing to subscribe to. Select one through the API and the worker will start streaming without a restart.");
                    await ReportAsync(RunState.Idle, "There is no Primary Channel, so there is nothing to stream.").ConfigureAwait(false);
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
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Reports a dropped stream and pauses before reconnecting.
    /// </summary>
    /// <remarks>
    /// This once called <c>StopApplication()</c>, because without Checkpoints a dropped stream could not be
    /// resumed without losing events. Checkpoints removed that cost: the reconnect resumes after the last
    /// fully applied batch, so nothing is lost as long as it happens within the 72 hours Salesforce keeps
    /// events for. Credentials are managed through the API this host serves, so the host stays up to be
    /// repaired rather than shutting down.
    /// </remarks>
    private async Task ReportDropAndPause(RpcException exc, CancellationToken stoppingToken) {
        _logger.LogError(exc,
            "The Salesforce event stream dropped ({Status}): {Message}. Reconnecting in {Delay}s and resuming " +
            "from the Checkpoint, so events published meanwhile will be replayed.",
            exc.StatusCode, exc.Message, RetryDelay.TotalSeconds);

        await ReportRetryingAsync($"The Salesforce event stream dropped ({exc.StatusCode}): {exc.Status.Detail.TrimEnd('.')}. " +
            "Reconnecting from the Checkpoint.").ConfigureAwait(false);
        await Task.Delay(RetryDelay, _time, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the write failure on the Target Connection and ends the stream.
    /// </summary>
    /// <remarks>
    /// Dropping rather than holding the stream open and logging per event. Holding it open would consume every
    /// event in the outage window into a database that cannot store them, while hammering that database. The
    /// batch that failed never reaches its Checkpoint, so once the plan loop finds the database back it resumes
    /// from the start of that batch and nothing is lost — unless the outage outlasts the 72 hours Salesforce
    /// keeps events for, which the expired-Checkpoint fallback reports.
    /// </remarks>
    private async Task ReportTargetFailureAndDrop(TargetDatabaseWriteException exc, CancellationToken stoppingToken) {
        _logger.LogCritical(exc,
            "The Target Database refused a write, so the stream has been dropped: {Message}. Once it recovers the " +
            "worker resumes from the Checkpoint and replays everything since, provided that is within 72 hours.",
            exc.Message);

        await ReportAsync(RunState.Retrying,
            $"The Target Database refused a write: {exc.Message}. Waiting for it to come back, then replaying from the Checkpoint.")
            .ConfigureAwait(false);

        try {
            using var scope = _scopeFactory.CreateScope();
            var targets = scope.ServiceProvider.GetRequiredService<ITargetConnectionService>();
            await targets.RecordWriteFailureAsync(exc.InnerException ?? exc, stoppingToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Could not record the Target Connection failure; the state may still read Connected");
        }
    }

    /// <summary>
    /// Reports that an event's Avro Schema or Binding could not be looked up, and pauses before resubscribing.
    /// </summary>
    /// <remarks>
    /// Salesforce or the App Database not answering right now is not a problem with the event, so nothing is
    /// skipped: the batch never reaches its Checkpoint and is replayed once the lookup works again.
    /// </remarks>
    private async Task ReportSchemaUnavailableAndPause(SchemaUnavailableException exc, CancellationToken stoppingToken) {
        _logger.LogError(exc, "{Message} Reconnecting in {Delay}s and replaying from the Checkpoint.",
            exc.Message, RetryDelay.TotalSeconds);
        await ReportRetryingAsync($"{exc.Message} Replaying from the Checkpoint.").ConfigureAwait(false);
        await Task.Delay(RetryDelay, _time, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>Proves the Failed Target Connection again. Success is recorded, and logged loudly, by the service.</summary>
    private async Task VerifyTargetAsync(CancellationToken stoppingToken) {
        try {
            using var scope = _scopeFactory.CreateScope();
            var targets = scope.ServiceProvider.GetRequiredService<ITargetConnectionService>();
            await targets.VerifyAsync(stoppingToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Could not verify the Target Connection; will retry");
        }
    }

    /// <summary>
    /// Sets the Inbound Run State and tells the Incident recorder, which never throws. Recorded without the
    /// stopping token, so the last change before a shutdown is not lost to it.
    /// </summary>
    private Task ReportAsync(RunState state, string? reason = null, DateTimeOffset? nextAttemptAt = null) {
        _health.Set(state, reason, nextAttemptAt);
        return _incidents.ReportAsync(RunDirection.Inbound, state, reason);
    }

    /// <summary>Reports Retrying with when the next attempt is, <see cref="RetryDelay"/> from now.</summary>
    private Task ReportRetryingAsync(string reason) =>
        ReportAsync(RunState.Retrying, reason, _time.GetUtcNow() + RetryDelay);

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
            var start = plan.StartPosition;
            while (true) {
                try {
                    await StreamFrom(start, plan, bindings, planToken).ConfigureAwait(false);
                    break;
                } catch (RpcException exc) when (start.Checkpoint is { } rejected && IsReplayIdRejected(exc)) {
                    await DiscardRejectedCheckpoint(plan, rejected, exc, planToken).ConfigureAwait(false);
                    start = StartPosition.Earliest;
                }
            }
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Configuration changed; rebuilding the subscription plan");
        } catch (RpcException exc) when (exc.StatusCode == StatusCode.Cancelled && !stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Configuration changed; rebuilding the subscription plan");
        } catch (RpcException exc) when (exc.StatusCode == StatusCode.Cancelled && stoppingToken.IsCancellationRequested) {
            // The application is stopping and cancelled the call itself: not a drop, and not an Incident.
        } catch (RpcException exc) {
            await ReportDropAndPause(exc, stoppingToken).ConfigureAwait(false);
        } catch (TargetDatabaseWriteException exc) {
            await ReportTargetFailureAndDrop(exc, stoppingToken).ConfigureAwait(false);
        } catch (SchemaUnavailableException exc) {
            await ReportSchemaUnavailableAndPause(exc, stoppingToken).ConfigureAwait(false);
        } finally {
            if (!planScope.IsCancellationRequested) {
                await planScope.CancelAsync().ConfigureAwait(false);
            }
            await watcher.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens one subscription at <paramref name="start"/> and applies what arrives until it ends.
    /// </summary>
    /// <remarks>
    /// Salesforce reads the replay position only from a stream's first FetchRequest, so moving to another
    /// position always means a new subscription.
    /// </remarks>
    private async Task StreamFrom(StartPosition start, SubscriptionPlan plan, Dictionary<string, CDCSchema> bindings,
        CancellationToken planToken) {
        var fetchRequest = new FetchRequest {
            TopicName = plan.TopicName,
            NumRequested = EventsPerFetch
        };

        switch (start.From) {
            case StartFrom.Resume when start.Checkpoint is { } checkpoint:
                fetchRequest.ReplayPreset = ReplayPreset.Custom;
                fetchRequest.ReplayId = ByteString.CopyFrom(checkpoint.ReplayId);
                break;
            case StartFrom.Earliest:
                fetchRequest.ReplayPreset = ReplayPreset.Earliest;
                break;
            default:
                fetchRequest.ReplayPreset = ReplayPreset.Latest;
                break;
        }

        _logger.LogInformation("Subscribing to {Topic} from {Start} with {Count} active binding(s)",
            plan.TopicName, start.From, bindings.Count);

        using var stream = _pubsubClient.Subscribe(null, null, planToken);
        await stream.RequestStream.WriteAsync(fetchRequest, planToken).ConfigureAwait(false);

        // With no Active Binding the stream still runs, so the Checkpoint keeps moving past events nobody wants,
        // but nothing is being synced: that is Idle, by configuration.
        if (bindings.Count == 0) {
            await ReportAsync(RunState.Idle, $"Primary Channel {plan.ChannelFullName} has no Active Binding.").ConfigureAwait(false);
        } else {
            await ReportAsync(RunState.Running).ConfigureAwait(false);
        }

        while (await stream.ResponseStream.MoveNext(planToken).ConfigureAwait(false)) {
            var response = stream.ResponseStream.Current;
            _health.Heard(_time.GetUtcNow());
            _logger.LogInformation("Latest Replay Id: {replayId}, RPC Id: {RpcId}",
                response.LatestReplayId.ToLongBE(), response.RpcId);

            if (response.Events is { Count: > 0 }) {
                await ApplyBatch(response.Events, bindings, plan.ChannelFullName!, planToken).ConfigureAwait(false);
            }

            // A strategy can swallow a cancellation as one record's failure, so a batch that "finished"
            // after the plan was cancelled may not have been applied. It is replayed rather than trusted.
            planToken.ThrowIfCancellationRequested();
            await SaveCheckpoint(plan, response.LatestReplayId, planToken).ConfigureAwait(false);

            await TopUpRequests(stream.RequestStream, plan.TopicName!, response.PendingNumRequested, planToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The Salesforce error code for a replay ID it will not resume from: older than the 72-hour retention
    /// window, or never issued for this topic.
    /// </summary>
    private const string ReplayIdCorruptedCode = "sfdc.platform.eventbus.grpc.subscription.fetch.replayid.corrupted";

    /// <summary>Whether Salesforce ended the subscription because it rejected the replay ID it was given.</summary>
    /// <remarks>
    /// The code arrives in the <c>error-code</c> trailer. The status detail is checked as well, so a proxy that
    /// drops trailers does not turn an expired Checkpoint into an endless drop-and-retry loop.
    /// </remarks>
    private static bool IsReplayIdRejected(RpcException exc) =>
        string.Equals(exc.Trailers.GetValue("error-code"), ReplayIdCorruptedCode, StringComparison.Ordinal)
        || exc.Status.Detail?.Contains(ReplayIdCorruptedCode, StringComparison.Ordinal) == true;

    /// <summary>
    /// Reports and discards a Checkpoint Salesforce will no longer resume from.
    /// </summary>
    /// <remarks>
    /// Every event between the Checkpoint and the oldest event Salesforce still keeps is gone, and nothing
    /// can bring it back, so this is Critical. The caller resumes from Earliest whatever the Channel's
    /// Starting Point says, to recover everything still available. The Checkpoint is discarded with Earliest
    /// recorded as the restart position, so a restart before the next save neither hits the same rejection
    /// nor falls back to a Latest Starting Point. If the discard fails, the first save from the new stream
    /// replaces the Checkpoint anyway.
    /// </remarks>
    private async Task DiscardRejectedCheckpoint(SubscriptionPlan plan, Checkpoint rejected, RpcException exc,
        CancellationToken cancellationToken) {
        var age = DateTime.UtcNow - DateTime.SpecifyKind(rejected.SavedAt, DateTimeKind.Utc);
        _logger.LogCritical(exc,
            "Salesforce rejected the Checkpoint for {Channel}, saved {AgeHours:F1} hours ago; it keeps change events for " +
            "{RetentionHours} hours. Changes made between that Checkpoint and the oldest event Salesforce still keeps " +
            "have been lost. Resubscribing from Earliest to recover everything still available.",
            plan.ChannelFullName, age.TotalHours, Checkpoint.Retention.TotalHours);
        await _incidents.CheckpointDiscardedAsync(
            $"Salesforce rejected the Checkpoint for {plan.ChannelFullName}, saved {age.TotalHours:F1} hours ago; it keeps " +
            $"change events for {Checkpoint.Retention.TotalHours:F0} hours. Changes made between that Checkpoint and the " +
            "oldest event Salesforce still keeps are missing from the Target Database. Syncing restarted from Earliest.")
            .ConfigureAwait(false);

        try {
            await _checkpoints.DiscardAsync(rejected.ChannelId, StartingPoint.Earliest, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Could not discard the rejected Checkpoint for {Channel}; the next save replaces it",
                plan.ChannelFullName);
        }
    }

    /// <summary>
    /// Records that every event up to <paramref name="latestReplayId"/> has been applied or deliberately
    /// skipped, so the next subscription resumes after it.
    /// </summary>
    /// <remarks>
    /// Called after a whole batch and after a keepalive, whose position Salesforce advances while the Channel
    /// is quiet — without that, a Channel with no activity for three days would end up with an expired
    /// Checkpoint. A failed save does not stop the stream: it only means a restart before the next successful
    /// save replays a little more, which idempotent writes make harmless (ADR 0005).
    /// </remarks>
    private async Task SaveCheckpoint(SubscriptionPlan plan, ByteString latestReplayId, CancellationToken cancellationToken) {
        if (plan.ChannelId is not int channelId || latestReplayId.IsEmpty) {
            return;
        }

        try {
            await _checkpoints.SaveAsync(channelId, latestReplayId.ToByteArray(), cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex,
                "Could not save the Checkpoint for {Channel}; streaming continues. A restart before the next " +
                "successful save will replay the events since the last one.",
                plan.ChannelFullName);
        }
    }

    /// <summary>
    /// Asks Salesforce for enough events to bring the outstanding request back up to one batch.
    /// </summary>
    /// <remarks>
    /// Salesforce delivers only what has been requested, so a subscriber that asks once and never again goes
    /// silent after its first batch. Requesting only after a batch is applied, and never beyond one batch
    /// outstanding, is the back-pressure: a slow Target Database slows the stream instead of letting events
    /// pile up in memory.
    /// </remarks>
    private static async Task TopUpRequests(IClientStreamWriter<FetchRequest> requests, string topicName,
        int pendingNumRequested, CancellationToken cancellationToken) {
        var shortfall = EventsPerFetch - pendingNumRequested;
        if (shortfall <= 0) {
            return;
        }

        await requests.WriteAsync(new FetchRequest { TopicName = topicName, NumRequested = shortfall },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WatchForConfigurationChange(CancellationTokenSource planScope) {
        // Subscribed to synchronously, before the caller opens the stream: waiting from inside a queued task
        // would miss a change signalled before that task first ran.
        var changed = _changeSignal.WaitForChangeAsync(planScope.Token);
        try {
            await changed.ConfigureAwait(false);
            await planScope.CancelAsync().ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // The stream ended first; nothing to do.
        }
    }

    /// <summary>One event, decoded and routed, ready for its strategy.</summary>
    private sealed record DecodedEvent(ConsumerEvent Source, CDCSchema Binding, GenericRecord Record, Schema Schema,
        ChangeType ChangeType, IReadOnlyList<string> RecordIds);

    /// <summary>
    /// Applies one response's events: every event touching a record after every earlier event touching it,
    /// and events for unrelated records in parallel.
    /// </summary>
    /// <remarks>
    /// Decoding runs first, for the whole batch, because only a decoded event says which records it touches.
    /// Events are then split into ordering groups — two events share a group when they share any record ID,
    /// transitively, since one bulk change can touch records that other events touch separately. Each group
    /// runs in delivery order and the groups run in parallel. Without this, a CREATE and the UPDATE after it
    /// race, and an UPDATE that wins matches no row and is lost; replaying from a Checkpoint delivers exactly
    /// these dense bursts.
    /// </remarks>
    private async Task ApplyBatch(IReadOnlyList<ConsumerEvent> events, Dictionary<string, CDCSchema> bindings,
        string channelFullName, CancellationToken cancellationToken) {
        var decoded = await Task.WhenAll(events.Select(e => DecodeEvent(e, bindings, channelFullName, cancellationToken)))
            .ConfigureAwait(false);

        var groups = OrderingGroups(decoded.OfType<DecodedEvent>().ToList());

        await Task.WhenAll(groups.Select(async group => {
            foreach (var decodedEvent in group) {
                await ApplyEvent(decodedEvent, channelFullName, cancellationToken).ConfigureAwait(false);
            }
        })).ConfigureAwait(false);
    }

    /// <summary>
    /// Splits events into groups that share record IDs, each group in delivery order.
    /// </summary>
    private static List<List<DecodedEvent>> OrderingGroups(IReadOnlyList<DecodedEvent> events) {
        // Union-find over event positions: every event is joined to the first earlier event that touched each of
        // its records, so a chain of shared records ends up under one root.
        var parent = Enumerable.Range(0, events.Count).ToArray();
        int Root(int i) {
            while (parent[i] != i) {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        var firstEventForRecord = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < events.Count; i++) {
            foreach (var recordId in events[i].RecordIds) {
                if (firstEventForRecord.TryGetValue(recordId, out var earlier)) {
                    parent[Root(i)] = Root(earlier);
                } else {
                    firstEventForRecord[recordId] = i;
                }
            }
        }

        // Positions are visited in order, so each group keeps delivery order.
        return Enumerable.Range(0, events.Count)
            .GroupBy(Root)
            .Select(g => g.Select(i => events[i]).ToList())
            .ToList();
    }

    /// <summary>
    /// Finds an event's Binding and decodes it, or returns null when the event is to be skipped.
    /// </summary>
    /// <remarks>
    /// Two kinds of failure, kept apart. Finding the Binding reads Salesforce and the App Database, so failing there
    /// says nothing about the event: it escapes as <see cref="SchemaUnavailableException"/>, the stream drops and
    /// the batch is replayed. A failure after that — a payload that will not decode, a header that cannot be read —
    /// is this event's problem: it is recorded as an Inbound Failure and the event skipped, so one bad record
    /// cannot stop the batch or the stream.
    /// </remarks>
    private async Task<DecodedEvent?> DecodeEvent(ConsumerEvent consumerEvent,
        Dictionary<string, CDCSchema> bindings, string channelFullName, CancellationToken cancellationToken) {
        _logger.LogInformation("Event Replay Id: {replayId}, Schema Id: {schemaId}",
            consumerEvent.ReplayId.ToLongBE(), consumerEvent.Event.SchemaId);

        var binding = await ResolveBinding(consumerEvent.Event.SchemaId, bindings, cancellationToken)
            .ConfigureAwait(false);

        if (binding is null) {
            return null;
        }

        string? changeTypeName = null;
        IReadOnlyList<string> recordIds = [];
        try {
            if (binding.AvroSchema?.SchemaJson is not { } schemaJson) {
                throw new InvalidOperationException(
                    $"Binding {binding.Id} has no Avro Schema to decode {binding.EntityName} with.");
            }

            var schema = Schema.Parse(schemaJson);

            using var memStream = new MemoryStream(consumerEvent.Event.Payload.ToByteArray());
            var decoder = new BinaryDecoder(memStream);
            var datumReader = new GenericDatumReader<GenericRecord>(schema, schema);
            var record = datumReader.Read(null!, decoder);

            if (!record.GetTypedValue<GenericRecord>("ChangeEventHeader", out var changeEventHeader) ||
                !changeEventHeader.GetTypedValue<GenericEnum>("changeType", out var changeType)) {
                throw new InvalidOperationException($"The event for {binding.EntityName} carries no readable ChangeEventHeader.");
            }

            changeTypeName = changeType.Value;
            recordIds = changeEventHeader.TryGetValue("recordIds", out var ids) && ids is object[] idArray
                ? idArray.Select(id => id?.ToString() ?? string.Empty).ToList()
                : [];

            if (!Enum.TryParse(changeType.Value, out ChangeType changeTypeEnum)) {
                throw new InvalidOperationException($"Unrecognised change type '{changeType.Value}' for {binding.EntityName}.");
            }

            return new DecodedEvent(consumerEvent, binding, record, schema, changeTypeEnum, recordIds);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to decode event with Schema Id {SchemaId}; skipping it and continuing with the rest of the batch",
                consumerEvent.Event.SchemaId);
            await RecordFailure(consumerEvent, binding, channelFullName, changeTypeName, recordIds, ex).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Applies one event through its strategy. A failure that is the event's own is recorded as an Inbound
    /// Failure and the batch carries on; a Target Database that refused the write ends the stream instead.
    /// </summary>
    private async Task ApplyEvent(DecodedEvent decodedEvent, string channelFullName, CancellationToken cancellationToken) {
        try {
            _logger.LogInformation("Processing {ChangeType} for {Entity}", decodedEvent.ChangeType, decodedEvent.Binding.EntityName);

            var strategy = _eventResolver.Resolve(decodedEvent.ChangeType);
            await strategy.ProcessEvent(decodedEvent.Record, decodedEvent.Schema, decodedEvent.Binding, cancellationToken)
                .ConfigureAwait(false);
            _health.Applied(_time.GetUtcNow());
        } catch (OperationCanceledException) {
            throw;
        } catch (TargetDatabaseWriteException) {
            throw;
        } catch (Exception ex) when (cancellationToken.IsCancellationRequested) {
            // A driver can report a cancelled command as an ordinary error. The batch is being abandoned and will
            // be replayed, so this is no Inbound Failure — recording one would leave a gap that is not there.
            throw new OperationCanceledException("The batch was cancelled while applying an event.", ex, cancellationToken);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to apply {ChangeType} with Schema Id {SchemaId}; continuing with the rest of the batch",
                decodedEvent.ChangeType, decodedEvent.Source.Event.SchemaId);
            await RecordFailure(decodedEvent.Source, decodedEvent.Binding, channelFullName,
                decodedEvent.ChangeType.ToString(), decodedEvent.RecordIds, ex).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records an event that is being skipped because it could not be applied, so the gap it leaves in the Target
    /// Database is never silent. A failure to record is logged and swallowed: it must not stop the batch.
    /// </summary>
    private async Task RecordFailure(ConsumerEvent consumerEvent, CDCSchema binding, string channelFullName,
        string? changeType, IReadOnlyList<string> recordIds, Exception error) {
        _health.Failed();
        var now = _time.GetUtcNow().UtcDateTime;
        try {
            // Without the batch's token: a skipped event must be recorded even if the batch is being cancelled.
            await _failures.RecordAsync(new InboundFailure {
                BindingId = binding.Id,
                ChannelFullName = channelFullName,
                ReplayId = consumerEvent.ReplayId.ToByteArray(),
                EntityName = binding.EntityName,
                ChangeType = changeType,
                RecordIds = recordIds.ToArray(),
                Message = error.Message,
                FirstFailedAt = now,
                LastFailedAt = now
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(ex,
                "Could not record the Inbound Failure for {Entity} (records {RecordIds}); the event is skipped and only this log says so",
                binding.EntityName, string.Join(", ", recordIds));
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

        try {
            return await ResolveUnknownSchema(schemaId, bindings, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            throw new SchemaUnavailableException(schemaId, ex);
        }
    }

    private async Task<CDCSchema?> ResolveUnknownSchema(string schemaId,
        Dictionary<string, CDCSchema> bindings, CancellationToken cancellationToken) {
        _logger.LogInformation("Unrecognised Schema Id {SchemaId}; fetching it from Salesforce", schemaId);

        var avroSchema = await FetchAndStoreSchema(schemaId, cancellationToken).ConfigureAwait(false);

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

    private async Task<DbAvroSchema> FetchAndStoreSchema(string schemaId, CancellationToken cancellationToken) {
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
    }
}
