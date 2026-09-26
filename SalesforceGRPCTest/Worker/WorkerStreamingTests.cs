using Microsoft.Extensions.Logging;
using NSubstitute;
using static SalesforceGRPCTest.Worker.WorkerHarness;

namespace SalesforceGRPCTest.Worker;

/// <summary>
/// The worker's streaming loop, driven from the outside: a scripted Pub/Sub server in, the Target Database
/// and the Checkpoint store out.
/// </summary>
public class WorkerStreamingTests {

    private static GrpcClient.ConsumerEvent[] Deletes(int count, int offset = 0) =>
        Enumerable.Range(offset, count).Select(i => Delete($"001{i:D15}")).ToArray();

    #region Flow control

    [Fact]
    public async Task KeepsRequestingEventsAsItAppliesThem_SoTheStreamNeverStallsAfterItsFirst25() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(
            Response(1, Deletes(25)),
            Response(2, Deletes(25, 25)),
            Response(3, Deletes(25, 50)));

        await harness.RunUntil(subscription);

        Assert.Equal(75, harness.Target.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "Delete"));
    }

    [Fact]
    public async Task NeverHasMoreThan25EventsRequestedAndUnprocessed() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(
            Response(1, Deletes(10)),
            Response(2, Deletes(25, 10)),
            Keepalive(3),
            Response(4, Deletes(5, 35)));

        await harness.RunUntil(subscription);

        Assert.Equal(25, subscription.MaxOutstanding);
        Assert.All(subscription.Requests, r => Assert.InRange(r.NumRequested, 1, 25));
    }

    #endregion

    #region Idempotent writes

    [Fact]
    public async Task ACreateIsWrittenAsAnUpsertOnTheKeyMappingColumn_SoAReplayedCreateCannotAddASecondRow() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(
            Response(1, Create("001A", "555-0100")),
            Response(2, Create("001A", "555-0100")));

        await harness.RunUntil(subscription);

        await harness.Target.Received(2).Upsert(Table, KeyColumn,
            Arg.Is<Dictionary<string, object>>(d => (string)d[KeyColumn] == "001A" && (string)d["phone"] == "555-0100"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ordering

    /// <summary>Records each write as it finishes, so a write that overtook another shows up out of order.</summary>
    private static List<string> RecordCompletions(WorkerHarness harness, TimeSpan createDelay, params string[] slowRecordIds) {
        var completed = new List<string>();
        harness.Target.Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(async call => {
                var recordId = (string)call.ArgAt<Dictionary<string, object>>(2)[KeyColumn];
                if (slowRecordIds.Contains(recordId)) {
                    await Task.Delay(createDelay);
                }
                lock (completed) { completed.Add($"create {recordId}"); }
                return 1;
            });
        harness.Target.Update(Table, KeyColumn, Arg.Any<List<string>>(), Arg.Any<Dictionary<string, object>>())
            .Returns(call => {
                lock (completed) { completed.Add($"update {string.Join(",", call.ArgAt<List<string>>(2))}"); }
                return 1;
            });
        harness.Target.Delete(Table, KeyColumn, Arg.Any<List<string>>())
            .Returns(call => {
                lock (completed) { completed.Add($"delete {string.Join(",", call.ArgAt<List<string>>(2))}"); }
                return 1;
            });
        return completed;
    }

    [Fact]
    public async Task ACreateAndALaterUpdateOfTheSameRecordInOneBatch_AreAppliedInThatOrder() {
        var harness = new WorkerHarness();
        var completed = RecordCompletions(harness, TimeSpan.FromMilliseconds(200), "001A");
        var subscription = harness.PubSub.Script(
            Response(1, Create("001A", "555-0100"), Update("555-0199", "001A")));

        await harness.RunUntil(subscription);

        Assert.Equal(["create 001A", "update 001A"], completed);
    }

    [Fact]
    public async Task AnEventThatChangesSeveralRecords_IsOrderedAgainstEveryEventTouchingAnyOfThem() {
        var harness = new WorkerHarness();
        var completed = RecordCompletions(harness, TimeSpan.FromMilliseconds(200), "001A");
        // The bulk delete joins 001A's events to 001B's, so the CREATE of 001B has to wait for both.
        var subscription = harness.PubSub.Script(
            Response(1, Create("001A", "1"), Delete("001A", "001B"), Create("001B", "2")));

        await harness.RunUntil(subscription);

        Assert.Equal(["create 001A", "delete 001A,001B", "create 001B"], completed);
    }

    [Fact]
    public async Task EventsForUnrelatedRecords_DoNotWaitForEachOther() {
        var harness = new WorkerHarness();
        var releaseA = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Target.Upsert(Table, KeyColumn, Arg.Is<Dictionary<string, object>>(d => (string)d[KeyColumn] == "001A"),
            Arg.Any<CancellationToken>()).Returns(_ => releaseA.Task);
        harness.Target.Upsert(Table, KeyColumn, Arg.Is<Dictionary<string, object>>(d => (string)d[KeyColumn] == "001B"),
            Arg.Any<CancellationToken>()).Returns(_ => { bWritten.TrySetResult(); return 1; });
        var subscription = harness.PubSub.Script(Response(1, Create("001A", "1"), Create("001B", "2")));

        // 001B is written while 001A is still stuck; only then is 001A let go.
        await harness.RunUntil(async () => {
            await bWritten.Task;
            releaseA.SetResult(1);
            await subscription.Drained.Task;
        });
    }

    [Fact]
    public async Task BatchesAreAppliedOneAfterAnother() {
        var harness = new WorkerHarness();
        var completed = RecordCompletions(harness, TimeSpan.FromMilliseconds(200), "001A");
        var subscription = harness.PubSub.Script(
            Response(1, Create("001A", "1")),
            Response(2, Create("001B", "2")));

        await harness.RunUntil(subscription);

        Assert.Equal(["create 001A", "create 001B"], completed);
    }

    #endregion

    #region Saving the Checkpoint

    [Fact]
    public async Task SavesTheBatchesLatestReplayIdAsTheCheckpoint_OnceEveryEventIsApplied() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(
            Response(1, Create("001A", "1")),
            Response(2, Create("001B", "2")));

        await harness.RunUntil(subscription);

        Received.InOrder(() => {
            harness.Target.Upsert(Table, KeyColumn, Arg.Is<Dictionary<string, object>>(d => (string)d[KeyColumn] == "001A"), Arg.Any<CancellationToken>());
            harness.Checkpoints.SaveAsync(ChannelId, IsReplayId(1), Arg.Any<CancellationToken>());
            harness.Target.Upsert(Table, KeyColumn, Arg.Is<Dictionary<string, object>>(d => (string)d[KeyColumn] == "001B"), Arg.Any<CancellationToken>());
            harness.Checkpoints.SaveAsync(ChannelId, IsReplayId(2), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SavesAKeepalivesLatestReplayId_SoAQuietChannelsCheckpointKeepsAdvancing() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(Keepalive(9));

        await harness.RunUntil(subscription);

        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(9), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MovesTheCheckpointPastAnEventThatCannotBeDecoded() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(Response(3, Garbage(), Create("001A", "1")));

        await harness.RunUntil(subscription);

        await harness.Target.Received(1).Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotSaveTheCheckpoint_WhenTheTargetDatabaseRefusesAWrite_SoTheBatchIsReplayed() {
        var harness = new WorkerHarness();
        harness.Target.Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UnreachableDatabaseException());
        harness.PubSub.Script(Response(1, Create("001A", "1")));

        await harness.RunUntil(() => harness.PubSub.WaitForSubscriptionsAsync(2));

        await harness.Checkpoints.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default);
    }

    [Fact]
    public async Task DoesNotSaveTheCheckpoint_WhenAConfigurationChangeInterruptsTheBatch() {
        var harness = new WorkerHarness();
        harness.Target.Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(async call => {
                harness.Signal.Signal();
                await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(3));
                return 1;
            });
        harness.PubSub.Script(Response(1, Create("001A", "1")));

        await harness.RunUntil(() => harness.PubSub.WaitForSubscriptionsAsync(2));

        await harness.Checkpoints.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default);
    }

    [Fact]
    public async Task KeepsStreaming_WhenSavingTheCheckpointFails_AndSaysSoInTheLog() {
        var harness = new WorkerHarness();
        harness.Checkpoints.SaveAsync(ChannelId, IsReplayId(1), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("App Database unavailable")));
        var subscription = harness.PubSub.Script(
            Response(1, Create("001A", "1")),
            Response(2, Create("001B", "2")));

        await harness.RunUntil(subscription);

        await harness.Target.Received(2).Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(2), Arg.Any<CancellationToken>());
        Assert.Contains(harness.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Sales__chn"));
    }

    #endregion

    #region Where a subscription starts

    private static Database.Models.Checkpoint StoredCheckpoint(TimeSpan age) => new() {
        ChannelId = ChannelId, ReplayId = ReplayId(5).ToByteArray(), SavedAt = DateTime.UtcNow - age
    };

    [Fact]
    public async Task ResumesRightAfterTheStoredCheckpoint() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with {
            StartPosition = Application.Bindings.StartPosition.ResumeAfter(StoredCheckpoint(TimeSpan.FromHours(1)))
        };
        var subscription = harness.PubSub.Script(Keepalive(6));

        await harness.RunUntil(subscription);

        var first = subscription.Requests[0];
        Assert.Equal(GrpcClient.ReplayPreset.Custom, first.ReplayPreset);
        Assert.Equal(ReplayId(5), first.ReplayId);
    }

    [Theory]
    [InlineData(Application.Bindings.StartFrom.Earliest, GrpcClient.ReplayPreset.Earliest)]
    [InlineData(Application.Bindings.StartFrom.Latest, GrpcClient.ReplayPreset.Latest)]
    public async Task StartsAtTheEndOfTheStreamThePlanNames_WhenThereIsNoCheckpoint(
        Application.Bindings.StartFrom from, GrpcClient.ReplayPreset expected) {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with { StartPosition = new Application.Bindings.StartPosition(from) };
        var subscription = harness.PubSub.Script(Keepalive(1));

        await harness.RunUntil(subscription);

        Assert.Equal(expected, subscription.Requests[0].ReplayPreset);
        Assert.True(subscription.Requests[0].ReplayId.IsEmpty);
    }

    [Fact]
    public async Task WhenSalesforceRejectsTheCheckpoint_DiscardsItAndStartsAgainFromEarliestStraightAway() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with {
            StartPosition = Application.Bindings.StartPosition.ResumeAfter(StoredCheckpoint(TimeSpan.FromHours(80)))
        };
        harness.PubSub.ScriptFailure(ReplayIdCorrupted());
        var recovered = harness.PubSub.Script(Response(9, Create("001A", "1")));

        // Well inside the usual retry delay, so a pause before resubscribing would time this out.
        await harness.RunUntil(recovered);

        Assert.Equal(GrpcClient.ReplayPreset.Earliest, recovered.Requests[0].ReplayPreset);
        await harness.Checkpoints.Received(1).DiscardAsync(ChannelId, Database.Models.StartingPoint.Earliest, Arg.Any<CancellationToken>());
        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(9), Arg.Any<CancellationToken>());
        Assert.Contains(harness.Log.Entries, e => e.Level == LogLevel.Critical
            && e.Message.Contains("Sales__chn") && e.Message.Contains("80"));
    }

    [Fact]
    public async Task FallsBackToEarliest_EvenWhenTheChannelsStartingPointIsLatest() {
        var harness = new WorkerHarness();
        harness.Plan = harness.Plan with {
            StartPosition = Application.Bindings.StartPosition.ResumeAfter(StoredCheckpoint(TimeSpan.FromHours(80)))
        };
        harness.PubSub.ScriptFailure(ReplayIdCorrupted());
        harness.PubSub.Script();

        await harness.RunUntil(() => harness.PubSub.WaitForSubscriptionsAsync(2));

        // Re-planning would have read the Starting Point; the fallback must not go back to the plan for it.
        await harness.Bindings.Received(1).GetSubscriptionPlanAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>What Salesforce sends back for a replay ID it no longer keeps, or never issued.</summary>
    private static Grpc.Core.RpcException ReplayIdCorrupted() => new(
        new Grpc.Core.Status(Grpc.Core.StatusCode.InvalidArgument, "The Replay ID validation failed."),
        new Grpc.Core.Metadata { { "error-code", "sfdc.platform.eventbus.grpc.subscription.fetch.replayid.corrupted" } });

    #endregion
}
