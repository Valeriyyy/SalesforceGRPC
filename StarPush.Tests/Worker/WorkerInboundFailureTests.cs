using Application.Runtime;
using Database.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static StarPush.Tests.Worker.WorkerHarness;

namespace StarPush.Tests.Worker;

/// <summary>
/// Events that could not be applied are recorded as Inbound Failures rather than vanishing; events skipped on
/// purpose, and failures that may pass, are not.
/// </summary>
public class WorkerInboundFailureTests {
    private static InboundFailure IsFailure(Func<InboundFailure, bool> match) => Arg.Is<InboundFailure>(f => match(f));

    [Fact]
    public async Task AnEventThatCannotBeDecoded_IsRecordedWithWhateverCouldBeRead() {
        var harness = new WorkerHarness();
        var subscription = harness.PubSub.Script(Response(3, Garbage(), Create("001A", "1")));

        await harness.RunUntil(subscription);

        await harness.Failures.Received(1).RecordAsync(IsFailure(f =>
            f.BindingId == BindingId
            && f.ChannelFullName == "Sales__chn"
            && f.ReplayId.SequenceEqual(ReplayId(0).ToByteArray())
            && f.EntityName == "AccountChangeEvent"
            && f.ChangeType == null
            && f.RecordIds.Length == 0
            && f.Message.Length > 0), Arg.Any<CancellationToken>());
        Assert.Equal(1, harness.Health.FailuresRecorded);
    }

    [Fact]
    public async Task AnEventItsStrategyFailsOn_IsRecordedWithItsChangeTypeAndRecords_AndTheBatchCarriesOn() {
        var harness = new WorkerHarness();
        harness.Meta.GetCachedMapping(BindingId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mapping is corrupt"));
        var subscription = harness.PubSub.Script(Response(4, Update("555", "001A", "001B")));

        await harness.RunUntil(subscription);

        await harness.Failures.Received(1).RecordAsync(IsFailure(f =>
            f.ChangeType == "UPDATE"
            && f.RecordIds.SequenceEqual(new[] { "001A", "001B" })
            && f.Message.Contains("mapping is corrupt")), Arg.Any<CancellationToken>());
        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(4), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CountsEveryEventApplied_AndWhenTheLastOneWas() {
        var harness = new WorkerHarness();
        var before = DateTimeOffset.UtcNow;
        var subscription = harness.PubSub.Script(Response(1, Create("001A", "1"), Create("001B", "2")));

        await harness.RunUntil(subscription);

        Assert.Equal(2, harness.Health.EventsApplied);
        Assert.True(harness.Health.LastAppliedAt >= before);
        await harness.Failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task AFailureToRecordAnInboundFailure_DoesNotStopTheBatch() {
        var harness = new WorkerHarness();
        harness.Failures.RecordAsync(Arg.Any<InboundFailure>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("App Database unavailable"));
        var subscription = harness.PubSub.Script(Response(3, Garbage(), Create("001A", "1")));

        await harness.RunUntil(subscription);

        await harness.Target.Received(1).Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(3), Arg.Any<CancellationToken>());
        Assert.Contains(harness.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Inbound Failure"));
    }

    [Fact]
    public async Task AnEventForAnEntityWithNoBinding_IsSkippedOnPurpose_NotRecorded() {
        var harness = new WorkerHarness();
        harness.AvroSchemas.GetSchemaBySchemaIdAsync("SCHEMA_2", Arg.Any<CancellationToken>())
            .Returns(new DbAvroSchema { Id = 2, SchemaId = "SCHEMA_2", RecordName = "ContactChangeEvent", SchemaJson = SchemaJson });
        var subscription = harness.PubSub.Script(Response(2, WithSchema(Create("003A", "1"), "SCHEMA_2")));

        await harness.RunUntil(subscription);

        await harness.Failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATargetDatabaseOutage_PausesInsteadOfRecordingFailures() {
        var harness = new WorkerHarness();
        harness.Target.Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UnreachableDatabaseException());
        harness.PubSub.Script(Response(1, Create("001A", "1")));

        await harness.RunUntil(() => harness.PubSub.WaitForSubscriptionsAsync(2));

        await harness.Failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task ASchemaSalesforceCannotReturn_PausesAndReplays_InsteadOfSkippingTheEvent() {
        // The fake Pub/Sub server answers every schema request with an error, as Salesforce does while unreachable.
        var harness = new WorkerHarness { RetryDelay = TimeSpan.FromMilliseconds(20) };
        harness.PubSub.Script(Response(2, WithSchema(Create("001A", "1"), "SCHEMA_2")));

        await harness.RunUntil(() => harness.PubSub.WaitForSubscriptionsAsync(2));

        await harness.Checkpoints.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default);
        await harness.Failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        Assert.Contains(harness.IncidentStore.All, i => i.Kind == IncidentKind.Retrying && i.Message.Contains("SCHEMA_2"));
    }

    [Fact]
    public async Task AStrategyFailingBecauseTheBatchWasCancelled_IsNotAnInboundFailure_TheBatchIsReplayed() {
        var harness = new WorkerHarness();
        // A strategy that turns the cancellation into an ordinary error, the way a driver can.
        harness.Target.Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(async call => {
                harness.Signal.Signal();
                try {
                    await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(3));
                } catch (OperationCanceledException) {
                    throw new InvalidOperationException("command was cancelled");
                }
                return 1;
            });
        harness.PubSub.Script(Response(1, Create("001A", "1")));

        await harness.RunUntil(() => harness.PubSub.WaitForSubscriptionsAsync(2));

        await harness.Failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task ARowTheTargetDatabaseRefuses_IsAnInboundFailure_NotCountedAsApplied() {
        var harness = new WorkerHarness();
        harness.Target.Upsert(Table, KeyColumn, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new RefusedRowException());
        var subscription = harness.PubSub.Script(Response(5, Create("001A", "1")));

        await harness.RunUntil(subscription);

        await harness.Failures.Received(1).RecordAsync(IsFailure(f =>
            f.ChangeType == "CREATE" && f.RecordIds.SequenceEqual(new[] { "001A" }) && f.Message.Contains("too long")),
            Arg.Any<CancellationToken>());
        Assert.Equal(0, harness.Health.EventsApplied);
        await harness.Checkpoints.Received(1).SaveAsync(ChannelId, IsReplayId(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnActiveBindingWithNoAvroSchema_RecordsAnInboundFailure() {
        var harness = new WorkerHarness();
        var binding = Binding();
        binding.AvroSchema = null;
        harness.Plan = harness.Plan with { ActiveBindingsBySchemaId = new() { [SchemaId] = binding } };
        var subscription = harness.PubSub.Script(Response(6, Create("001A", "1")));

        await harness.RunUntil(subscription);

        await harness.Failures.Received(1).RecordAsync(IsFailure(f => f.Message.Contains("no Avro Schema")),
            Arg.Any<CancellationToken>());
    }
}
