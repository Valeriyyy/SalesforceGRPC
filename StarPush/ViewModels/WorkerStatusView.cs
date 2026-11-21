using Application.Runtime;
using Application.Writeback;

namespace StarPush.ViewModels;

/// <summary>
/// The Overview's worker card: what each direction of syncing is doing now, and what has happened lately. Also
/// the answer of <c>GET api/worker-status</c>, which the card polls.
/// </summary>
/// <param name="Writeback">Null when no Binding is Two-way.</param>
/// <param name="LatestIncident">The latest Incident of the last 24 hours, or null.</param>
/// <param name="OtherIncidents">How many more there were in the last 24 hours.</param>
public sealed record WorkerStatusView(
    DateTimeOffset StartedAt,
    InboundRunView Inbound,
    WritebackRunView? Writeback,
    IncidentView? LatestIncident,
    int OtherIncidents,
    int InboundFailures,
    int WritebackFailures) {

    public static WorkerStatusView For(WorkerStatus status, DateTimeOffset now) => new(
        status.Inbound.StartedAt,
        InboundRunView.For(status.Inbound, now),
        status.Writeback is { } writeback ? WritebackRunView.For(writeback) : null,
        status.RecentIncidents.Count > 0 ? IncidentView.For(status.RecentIncidents[0]) : null,
        Math.Max(0, status.RecentIncidents.Count - 1),
        status.InboundFailures,
        status.WritebackFailures);

    /// <summary>True when a direction is Blocked or Retrying — not merely starting, and not Idle by configuration.</summary>
    internal bool IsNotSyncing() =>
        Inbound.State is RunStateView.Blocked or RunStateView.Retrying
        || Writeback?.State is RunStateView.Blocked or RunStateView.Retrying;

    internal static RunStateView ViewOf(RunState state) => state switch {
        RunState.Idle => RunStateView.Idle,
        RunState.Blocked => RunStateView.Blocked,
        RunState.Running => RunStateView.Running,
        _ => RunStateView.Retrying
    };
}

/// <param name="Stale">
/// Running, but nothing — not even a keepalive — heard from Salesforce for longer than it should take: the stream
/// may be silently dead.
/// </param>
public sealed record InboundRunView(
    RunStateView State,
    string? Reason,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LastHeardAt,
    bool Stale,
    long EventsApplied,
    DateTimeOffset? LastAppliedAt) {

    public static InboundRunView For(InboundRun run, DateTimeOffset now) => new(
        run.Starting ? RunStateView.Starting : WorkerStatusView.ViewOf(run.State),
        run.Starting ? null : run.Reason,
        run.Starting ? null : run.NextAttemptAt,
        run.LastHeardAt,
        IsStale(run, now),
        run.EventsApplied,
        run.LastAppliedAt);

    /// <summary>
    /// Running, with nothing heard since the later of the last word from Salesforce and the stream opening — so a
    /// stream just reopened after a long outage is not called stuck before it has had the chance to speak.
    /// </summary>
    private static bool IsStale(InboundRun run, DateTimeOffset now) {
        if (run.Starting || run.State != RunState.Running) {
            return false;
        }
        var lastSign = run.LastHeardAt is { } heard && heard > run.StateSince ? heard : run.StateSince;
        return now - lastSign > InboundHealth.StaleAfter;
    }
}

/// <param name="LagBytes">How far behind the Target Database Writeback is; only when that is worth a look.</param>
public sealed record WritebackRunView(
    RunStateView State,
    string? Reason,
    DateTimeOffset? LastAppliedAt,
    long? LagBytes) {

    public static WritebackRunView For(WritebackRun run) => new(
        WorkerStatusView.ViewOf(run.State),
        run.Reason,
        run.LastAppliedAt,
        run.LagBytes > WritebackHealth.LagAttentionBytes ? run.LagBytes : null);
}
