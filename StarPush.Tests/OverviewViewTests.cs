using Application.Pipeline;
using Application.Runtime;
using Application.Writeback;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StarPush.ViewModels;

namespace StarPush.Tests;

/// <summary>
/// The Overview's worker card and header, built from the Pipeline and the workers' status. Asserts what the user
/// is shown, never how it was read.
/// </summary>
public class OverviewViewTests {
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static PipelineStatus Pipeline(StageStatus bindings = StageStatus.Ok) => new(
        new OrgConnectionStage { Status = StageStatus.Ok, OrgUrl = "https://acme.my.salesforce.com" },
        new PrimaryChannelStage { Status = StageStatus.Ok, ChannelFullName = "Sales__chn", MemberCount = 2 },
        new TargetConnectionStage { Status = StageStatus.Ok, Engine = TargetDatabaseEngine.Postgres, Host = "db", DatabaseName = "warehouse" },
        new BindingsStage {
            Status = bindings,
            Reasons = bindings == StageStatus.Ok ? [] : [StageReason.NoActiveBindings],
            Counts = new BindingStateCounts { Active = bindings == StageStatus.Ok ? 2 : 0 }
        });

    private static InboundRun Inbound(RunState state, bool starting = false, DateTimeOffset? lastHeardAt = null) => new() {
        State = state, Starting = starting, StartedAt = Now.AddHours(-3), LastHeardAt = lastHeardAt,
        Reason = state is RunState.Retrying or RunState.Blocked ? "something is wrong" : null
    };

    private static WorkerStatus Worker(InboundRun inbound, WritebackRun? writeback = null,
        IReadOnlyList<Incident>? incidents = null) =>
        new() { Inbound = inbound, Writeback = writeback, RecentIncidents = incidents ?? [] };

    private static OverviewView View(PipelineStatus pipeline, WorkerStatus worker) => OverviewView.For(pipeline, worker, Now);

    #region The header

    [Fact]
    public void EveryStageOk_AndRunning_IsSetUpAndSyncing() {
        var view = View(Pipeline(), Worker(Inbound(RunState.Running, lastHeardAt: Now.AddMinutes(-1))));

        Assert.True(view.AllOk);
        Assert.False(view.NotSyncing);
    }

    [Theory]
    [InlineData(RunState.Retrying)]
    [InlineData(RunState.Blocked)]
    public void EveryStageOk_ButInboundNotMoving_IsNotSyncing(RunState state) {
        var view = View(Pipeline(), Worker(Inbound(state)));

        Assert.True(view.AllOk);
        Assert.True(view.NotSyncing);
    }

    [Fact]
    public void EveryStageOk_ButWritebackRetrying_IsNotSyncing() {
        var view = View(Pipeline(), Worker(Inbound(RunState.Running, lastHeardAt: Now),
            new WritebackRun { State = RunState.Retrying, Reason = "Salesforce answered 503" }));

        Assert.True(view.NotSyncing);
    }

    [Fact]
    public void RightAfterStarting_IsNotCalledNotSyncing() {
        var view = View(Pipeline(), Worker(Inbound(RunState.Retrying, starting: true)));

        Assert.False(view.NotSyncing);
        Assert.Equal(RunStateView.Starting, view.WorkerStatus.Inbound.State);
    }

    [Fact]
    public void AStageNotOk_LeavesTheHeaderToTheStages() {
        var view = View(Pipeline(StageStatus.ToDo), Worker(Inbound(RunState.Idle)));

        Assert.False(view.AllOk);
        Assert.False(view.NotSyncing);
    }

    #endregion

    #region The worker card

    [Fact]
    public void RunningButNothingHeardForMoreThanFiveMinutes_IsStale() {
        var view = View(Pipeline(), Worker(Inbound(RunState.Running, lastHeardAt: Now.AddMinutes(-6))));

        Assert.True(view.WorkerStatus.Inbound.Stale);
        Assert.Equal(RunStateView.Running, view.WorkerStatus.Inbound.State);
    }

    [Fact]
    public void RunningAndHeardRecently_IsNotStale() {
        var view = View(Pipeline(), Worker(Inbound(RunState.Running, lastHeardAt: Now.AddMinutes(-4))));

        Assert.False(view.WorkerStatus.Inbound.Stale);
    }

    [Fact]
    public void JustResubscribedAfterALongOutage_IsNotStale_UntilFiveMinutesWithoutAWord() {
        var inbound = Inbound(RunState.Running, lastHeardAt: Now.AddMinutes(-40)) with { StateSince = Now.AddMinutes(-1) };

        var view = View(Pipeline(), Worker(inbound));

        Assert.False(view.WorkerStatus.Inbound.Stale);
    }

    [Fact]
    public void OnlyRunningCanBeStale() {
        var view = View(Pipeline(), Worker(Inbound(RunState.Retrying, lastHeardAt: Now.AddHours(-1))));

        Assert.False(view.WorkerStatus.Inbound.Stale);
    }

    [Fact]
    public void WithoutATwoWayBinding_ThereIsNoWritebackRow() {
        var view = View(Pipeline(), Worker(Inbound(RunState.Running, lastHeardAt: Now)));

        Assert.Null(view.WorkerStatus.Writeback);
    }

    [Theory]
    [InlineData(1024L, null)]
    [InlineData(WritebackHealth.LagAttentionBytes + 1, WritebackHealth.LagAttentionBytes + 1)]
    public void WritebacksLag_IsShownOnlyWhenItIsHigh(long lag, long? shown) {
        var view = View(Pipeline(), Worker(Inbound(RunState.Running, lastHeardAt: Now),
            new WritebackRun { State = RunState.Running, LagBytes = lag }));

        Assert.Equal(shown, view.WorkerStatus.Writeback!.LagBytes);
    }

    [Fact]
    public void ShowsTheLatestIncident_AndHowManyOthersThereWere() {
        var incidents = new[] {
            new Incident { Id = 3, Kind = IncidentKind.Retrying, Direction = RunDirection.Inbound, Message = "stream dropped",
                StartedAt = Now.AddMinutes(-30).UtcDateTime, Occurrences = 7 },
            new Incident { Id = 2, Kind = IncidentKind.Started, Message = "The application started.",
                StartedAt = Now.AddHours(-3).UtcDateTime, EndedAt = Now.AddHours(-3).UtcDateTime },
            new Incident { Id = 1, Kind = IncidentKind.Stopped, Message = "The application stopped.",
                StartedAt = Now.AddHours(-4).UtcDateTime, EndedAt = Now.AddHours(-4).UtcDateTime }
        };

        var view = View(Pipeline(), Worker(Inbound(RunState.Running, lastHeardAt: Now), incidents: incidents));

        Assert.Equal(3, view.WorkerStatus.LatestIncident!.Id);
        Assert.Equal(7, view.WorkerStatus.LatestIncident.Occurrences);
        Assert.Equal(2, view.WorkerStatus.OtherIncidents);
    }

    [Fact]
    public async Task WritebackIdle_IsReportedAsNoWritebackAtAll() {
        var service = new WorkerStatusService(new InboundHealth(TimeProvider.System), new WritebackHealth(),
            Substitute.For<IInboundFailureRepository>(), Substitute.For<IWritebackFailureRepository>(),
            Substitute.For<IIncidentRepository>(), new FakeTimeProvider(Now));

        var status = await service.GetAsync(TestContext.Current.CancellationToken);

        Assert.Null(status.Writeback);
    }

    #endregion
}
