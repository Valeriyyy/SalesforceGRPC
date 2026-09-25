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
}
