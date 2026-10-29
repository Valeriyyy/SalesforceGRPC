using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StarPush.Telemetry;

/// <summary>
/// The traces and metrics StarPush emits about its own work, as opposed to what the framework instrumentation
/// already says about HTTP requests and the runtime.
/// </summary>
/// <remarks>
/// Static, like the framework's own sources: an <see cref="ActivitySource"/> or <see cref="Meter"/> costs
/// nothing until a listener subscribes, so emitting unconditionally is free when no exporter is configured.
/// The names below are what a dashboard filters on — renaming one breaks every saved query that uses it.
/// </remarks>
public static class StarPushTelemetry {
    public const string Name = "StarPush";

    public static readonly string Version =
        typeof(StarPushTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(Name, Version);
    public static readonly Meter Meter = new(Name, Version);

    /// <summary>Events Salesforce delivered, before routing or decoding.</summary>
    public static readonly Counter<long> EventsReceived = Meter.CreateCounter<long>(
        "starpush.events.received", "{event}", "Change events delivered by Salesforce");

    /// <summary>Events by what became of them; tagged <c>outcome</c>, and <c>entity</c>/<c>change_type</c> once known.</summary>
    public static readonly Counter<long> EventsProcessed = Meter.CreateCounter<long>(
        "starpush.events.processed", "{event}", "Change events by outcome: applied, skipped, undecodable or failed");

    public static readonly Histogram<double> EventDuration = Meter.CreateHistogram<double>(
        "starpush.event.duration", "s", "Time to apply one change event to the Target Database");

    public static readonly Histogram<double> BatchDuration = Meter.CreateHistogram<double>(
        "starpush.batch.duration", "s", "Time to decode and apply one batch of change events");

    public static readonly Counter<long> CheckpointSaves = Meter.CreateCounter<long>(
        "starpush.checkpoint.saves", "{save}", "Checkpoint saves by outcome: saved or failed");

    /// <summary>Subscriptions that ended other than by shutdown; tagged <c>reason</c>.</summary>
    public static readonly Counter<long> StreamInterruptions = Meter.CreateCounter<long>(
        "starpush.stream.interruptions", "{interruption}",
        "Subscriptions ended by a dropped stream, a Target Database failure, a rejected Checkpoint or a configuration change");

    /// <summary>1 while subscribed to Salesforce, 0 otherwise — the quickest answer to "is it streaming?".</summary>
    public static readonly UpDownCounter<long> ActiveStreams = Meter.CreateUpDownCounter<long>(
        "starpush.stream.active", "{stream}", "Open Salesforce Pub/Sub subscriptions");

    /// <summary>
    /// Starts the span covering one batch as the root of its own trace.
    /// </summary>
    /// <remarks>
    /// A batch is a unit of work in its own right, not part of whatever happened to be ambient on the worker's
    /// long-running loop; without clearing the current activity every batch could land in one endless trace.
    /// </remarks>
    public static Activity? StartBatch(string? topic, int eventCount) {
        Activity.Current = null;
        var activity = ActivitySource.StartActivity($"process {topic}", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "salesforce_pubsub");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.batch.message_count", eventCount);
        return activity;
    }

    public static void RecordException(this Activity? activity, Exception exception) {
        if (activity is null) {
            return;
        }
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }
}
