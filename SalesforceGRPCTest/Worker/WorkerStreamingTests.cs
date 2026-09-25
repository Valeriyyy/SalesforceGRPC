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
}
