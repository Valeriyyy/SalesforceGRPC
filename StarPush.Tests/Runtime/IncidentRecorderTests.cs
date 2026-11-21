using Application.Runtime;
using Database.Models;
using Microsoft.Extensions.Time.Testing;
using StarPush.Tests.Worker;

namespace StarPush.Tests.Runtime;

/// <summary>
/// The application starting and stopping, which no worker harness owns, driven through the recorder over an
/// in-memory store.
/// </summary>
public class IncidentRecorderTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryIncidents _store = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

    private IncidentRecorder NewRun() => new(_store, _time, new ListLogger<IncidentRecorder>());

    [Fact]
    public async Task StartingIsAnIncident() {
        await NewRun().StartedAsync(Ct);

        var started = Assert.Single(_store.All);
        Assert.Equal(IncidentKind.Started, started.Kind);
        Assert.Null(started.Direction);
        Assert.DoesNotContain("unexpectedly", started.Message);
    }

    [Fact]
    public async Task AfterACleanStop_StartingSaysNothingAboutThePreviousRun() {
        var first = NewRun();
        await first.StartedAsync(Ct);
        await first.StoppedAsync(Ct);

        await NewRun().StartedAsync(Ct);

        Assert.Equal([IncidentKind.Started, IncidentKind.Stopped, IncidentKind.Started], _store.All.Select(i => i.Kind));
        Assert.DoesNotContain("unexpectedly", _store.All[^1].Message);
    }

    [Fact]
    public async Task AfterARunThatNeverStoppedCleanly_StartingSaysThePreviousRunEndedUnexpectedly() {
        await NewRun().StartedAsync(Ct);

        await NewRun().StartedAsync(Ct);

        Assert.Contains("ended unexpectedly", _store.All[^1].Message);
    }

    [Fact]
    public async Task StartingEndsWhateverThePreviousRunLeftGoingOn() {
        var first = NewRun();
        await first.StartedAsync(Ct);
        await first.ReportAsync(RunDirection.Inbound, RunState.Retrying, "stream dropped", Ct);

        _time.Advance(TimeSpan.FromMinutes(3));
        await NewRun().StartedAsync(Ct);

        var retrying = Assert.Single(_store.All, i => i.Kind == IncidentKind.Retrying);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, retrying.EndedAt);
    }

    [Fact]
    public async Task ARetryLongAfterRecovering_IsANewIncident() {
        var recorder = NewRun();
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Retrying, "stream dropped", Ct);
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Running, null, Ct);

        _time.Advance(IncidentRecorder.MergeWindow + TimeSpan.FromMinutes(1));
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Retrying, "stream dropped", Ct);

        Assert.Equal(2, _store.All.Count(i => i.Kind == IncidentKind.Retrying));
    }

    [Fact]
    public async Task EachDirectionHasItsOwnIncidents() {
        var recorder = NewRun();
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Retrying, "stream dropped", Ct);
        await recorder.ReportAsync(RunDirection.Writeback, RunState.Retrying, "Salesforce answered 503", Ct);
        await recorder.ReportAsync(RunDirection.Writeback, RunState.Running, null, Ct);

        Assert.Null(Assert.Single(_store.All, i => i.Direction == RunDirection.Inbound).EndedAt);
        Assert.NotNull(Assert.Single(_store.All, i => i.Direction == RunDirection.Writeback).EndedAt);
    }

    [Fact]
    public async Task AStoreThatIsDown_NeverThrows() {
        _store.Unavailable = true;
        var recorder = NewRun();

        await recorder.StartedAsync(Ct);
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Retrying, "stream dropped", Ct);
        await recorder.CheckpointDiscardedAsync("lost", Ct);
        await recorder.StoppedAsync(Ct);
    }

    [Fact]
    public async Task APipelineNeverSetUp_IsNotAnIncident_EvenAfterAFailedFirstAttempt() {
        var recorder = NewRun();
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Retrying, "App Database unavailable", Ct);
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Blocked, "There is no usable Org Connection.", Ct);

        var only = Assert.Single(_store.All);
        Assert.Equal(IncidentKind.Retrying, only.Kind);
        Assert.NotNull(only.EndedAt);
    }

    [Fact]
    public async Task ARetryThatTurnsOutToNeedTheUser_AfterRunning_IsABlockedIncident() {
        var recorder = NewRun();
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Running, null, Ct);
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Retrying, "stream dropped", Ct);
        await recorder.ReportAsync(RunDirection.Inbound, RunState.Blocked, "The Org Connection is Failed.", Ct);

        Assert.Equal([IncidentKind.Retrying, IncidentKind.Blocked], _store.All.Select(i => i.Kind));
    }
}
