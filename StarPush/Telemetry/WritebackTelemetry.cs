using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StarPush.Telemetry;

/// <summary>
/// The traces and metrics Writeback emits, under the same <c>StarPush</c> source and meter name as the rest of
/// the application's own telemetry, so one subscription collects both.
/// </summary>
/// <remarks>
/// Static: an <see cref="ActivitySource"/> or <see cref="Meter"/> costs nothing until a listener subscribes.
/// These names are what dashboards query and are documented in docs/observability.md — change both together.
/// </remarks>
public static class WritebackTelemetry {
    public const string Name = "StarPush";

    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);

    /// <summary>One Target Database transaction written back to Salesforce.</summary>
    public const string TransactionSpan = "writeback transaction";

    public static readonly Counter<long> ChangesRead = Meter.CreateCounter<long>(
        "starpush.writeback.changes.read", "{change}", "Target Table changes read for Writeback");

    public static readonly Counter<long> RecordsSent = Meter.CreateCounter<long>(
        "starpush.writeback.records.sent", "{record}", "Records Salesforce accepted from Writeback");

    public static readonly Counter<long> RecordsFailed = Meter.CreateCounter<long>(
        "starpush.writeback.records.failed", "{record}", "Records Salesforce refused, recorded as Writeback Failures");

    public static readonly Counter<long> Retries = Meter.CreateCounter<long>(
        "starpush.writeback.retries", "{retry}", "Times Writeback paused after a failure that may pass and sent its changes again");

    /// <summary>Registered by the host once, reading the Writeback health it is given.</summary>
    public static ObservableGauge<long> LagGauge(Func<long?> lagBytes) => Meter.CreateObservableGauge<long>(
        "starpush.writeback.lag",
        () => lagBytes() is { } bytes ? [new Measurement<long>(bytes)] : Array.Empty<Measurement<long>>(),
        "By", "How far Writeback is behind the Target Database");
}
