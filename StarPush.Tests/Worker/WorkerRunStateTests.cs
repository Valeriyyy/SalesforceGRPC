using Application.Runtime;
using Database.Models;
using NSubstitute;
using static StarPush.Tests.Worker.WorkerHarness;

namespace StarPush.Tests.Worker;

/// <summary>
/// The Inbound Run State the worker reports, and the Incidents it records, driven through the real worker over a
/// scripted Pub/Sub server.
/// </summary>
public class WorkerRunStateTests {
    [Fact]
    public async Task ReportsRunningOnceSubscribed_AndWhenSalesforceWasLastHeardFrom_EvenByAKeepalive() {
        var harness = new WorkerHarness();
        var before = DateTimeOffset.UtcNow;
        var subscription = harness.PubSub.Script(Keepalive(1));

        await harness.RunUntil(subscription);

        Assert.Equal(RunState.Running, harness.Health.State);
        Assert.False(harness.Health.Starting);
        Assert.NotNull(harness.Health.LastHeardAt);
        Assert.True(harness.Health.LastHeardAt >= before);
    }

    [Fact]
    public async Task IsBlocked_WithoutAUsableOrgConnection_AndSaysSo() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with { HasConnection = false };

        await harness.RunUntil(() => harness.UntilState(RunState.Blocked));

        Assert.Contains("Org Connection", harness.Health.Reason);
    }

    [Fact]
    public async Task IsBlocked_WithoutATargetConnection() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with { TargetConnectionState = null };

        await harness.RunUntil(() => harness.UntilState(RunState.Blocked));

        Assert.Contains("Target Connection", harness.Health.Reason);
    }

    [Fact]
    public async Task IsIdle_WithoutAPrimaryChannel() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with { TopicName = null, ChannelFullName = null, ChannelId = null };

        await harness.RunUntil(() => harness.UntilState(RunState.Idle));
    }

    [Fact]
    public async Task IsIdle_WhenThePrimaryChannelHasNoActiveBinding() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with { ActiveBindingsBySchemaId = new() };
        var subscription = harness.PubSub.Script(Keepalive(1));

        await harness.RunUntil(subscription);

        Assert.Equal(RunState.Idle, harness.Health.State);
    }

    [Fact]
    public async Task IsRetrying_WhileTheTargetConnectionIsFailed_WithWhenItTriesAgain() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with { TargetConnectionState = ConnectionState.Failed };

        await harness.RunUntil(() => harness.UntilState(RunState.Retrying));

        Assert.Contains("Target Connection", harness.Health.Reason);
        Assert.NotNull(harness.Health.NextAttemptAt);
    }

    [Fact]
    public async Task IsRetrying_WhenThePlanCannotBeBuilt_AndThatOpensAnIncident() {
        var harness = new WorkerHarness();
        harness.Bindings.GetSubscriptionPlanAsync(Arg.Any<CancellationToken>())
            .Returns<Application.Bindings.SubscriptionPlan>(_ => throw new InvalidOperationException("App Database unavailable"));

        await harness.RunUntil(() => harness.UntilState(RunState.Retrying, () => harness.IncidentStore.All.Count > 0));

        Assert.Contains("App Database unavailable", harness.Health.Reason);
        var incident = Assert.Single(harness.IncidentStore.All);
        Assert.Equal(IncidentKind.Retrying, incident.Kind);
        Assert.Equal(RunDirection.Inbound, incident.Direction);
        Assert.Null(incident.EndedAt);
    }

    [Fact]
    public async Task BeingBlockedFromTheStart_IsNotAnIncident_ItIsAPipelineNotSetUpYet() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with { HasConnection = false };

        await harness.RunUntil(() => harness.UntilState(RunState.Blocked));

        Assert.Empty(harness.IncidentStore.All);
    }

    private static Grpc.Core.RpcException Unavailable() =>
        new(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "connection reset"));

    [Fact]
    public async Task DropsBackToBack_AreOneIncident_CountingEachDrop_ThatEndsOnceTheStreamRecovers() {
        var harness = new WorkerHarness { RetryDelay = TimeSpan.FromMilliseconds(20) };
        harness.PubSub.ScriptFailure(Unavailable());
        harness.PubSub.ScriptFailure(Unavailable());
        harness.PubSub.ScriptFailure(Unavailable());
        var recovered = harness.PubSub.Script(Keepalive(1));

        await harness.RunUntil(() => Task.WhenAll(recovered.Drained.Task,
            harness.UntilState(RunState.Running, () => harness.IncidentStore.All.All(i => i.EndedAt is not null))));

        var incident = Assert.Single(harness.IncidentStore.All);
        Assert.Equal(IncidentKind.Retrying, incident.Kind);
        Assert.Equal(3, incident.Occurrences);
        Assert.Contains("dropped", incident.Message);
        Assert.NotNull(incident.EndedAt);
    }

    [Fact]
    public async Task ADroppedStream_IsRetrying_WithWhenItReconnects() {
        var harness = new WorkerHarness();
        harness.PubSub.ScriptFailure(Unavailable());

        await harness.RunUntil(() => harness.UntilState(RunState.Retrying));

        Assert.Contains("connection reset", harness.Health.Reason);
        Assert.NotNull(harness.Health.NextAttemptAt);
    }

    [Fact]
    public async Task StoppingBecauseSomethingNeedsTheUser_WhileRunning_IsAnIncident() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(Keepalive(1));

        await harness.RunUntil(async () => {
            await subscription.Drained.Task;
            harness.Plan = harness.Plan with { HasConnection = false };
            harness.Signal.Signal();
            await harness.UntilState(RunState.Blocked, () => harness.IncidentStore.All.Count > 0);
        });

        var incident = Assert.Single(harness.IncidentStore.All);
        Assert.Equal(IncidentKind.Blocked, incident.Kind);
        Assert.Contains("Org Connection", incident.Message);
        Assert.Null(incident.EndedAt);
    }

    [Fact]
    public async Task ATargetDatabaseThatRefusesAWrite_IsRetrying() {
        var harness = new WorkerHarness();
        harness.Target.Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UnreachableDatabaseException());
        harness.PubSub.Script(Response(1, Create("001A", "1")));
        // Once the write fails the Target Connection reads Failed, as the service would record it.
        harness.TargetConnections.When(t => t.RecordWriteFailureAsync(Arg.Any<Exception>(), Arg.Any<CancellationToken>()))
            .Do(_ => harness.Plan = harness.Plan with { TargetConnectionState = ConnectionState.Failed });

        await harness.RunUntil(() => harness.UntilState(RunState.Retrying, () => harness.IncidentStore.All.Count > 0));

        var incident = Assert.Single(harness.IncidentStore.All);
        Assert.Equal(IncidentKind.Retrying, incident.Kind);
    }

    [Fact]
    public async Task ACheckpointSalesforceRejects_IsAnIncident_BecauseChangesWereLost() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with {
            StartPosition = Application.Bindings.StartPosition.ResumeAfter(new Checkpoint {
                ChannelId = ChannelId, ReplayId = ReplayId(5).ToByteArray(), SavedAt = DateTime.UtcNow.AddHours(-80)
            })
        };
        harness.PubSub.ScriptFailure(new Grpc.Core.RpcException(
            new Grpc.Core.Status(Grpc.Core.StatusCode.InvalidArgument, "The Replay ID validation failed."),
            new Grpc.Core.Metadata { { "error-code", "sfdc.platform.eventbus.grpc.subscription.fetch.replayid.corrupted" } }));
        var recovered = harness.PubSub.Script(Keepalive(9));

        await harness.RunUntil(recovered);

        var incident = Assert.Single(harness.IncidentStore.All);
        Assert.Equal(IncidentKind.CheckpointDiscarded, incident.Kind);
        Assert.Contains("Sales__chn", incident.Message);
        Assert.NotNull(incident.EndedAt);
    }

    [Fact]
    public async Task StoppingTheApplication_IsNotADroppedStream() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(Keepalive(1));

        await harness.RunUntil(subscription);

        Assert.Empty(harness.IncidentStore.All);
        Assert.Equal(RunState.Running, harness.Health.State);
    }
}
