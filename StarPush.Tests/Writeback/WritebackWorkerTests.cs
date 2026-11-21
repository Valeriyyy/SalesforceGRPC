using Application.Writeback;
using Database.Models;
using Database.Writeback;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Salesforce.Writeback;
using static StarPush.Tests.Writeback.WritebackHarness;

namespace StarPush.Tests.Writeback;

/// <summary>
/// Writeback, driven from the outside: Target Table changes go in through a fake change stream, and the tests
/// assert what was sent to Salesforce, what was written back to the Target Database, what was recorded as a
/// Writeback Failure and which positions were confirmed.
/// </summary>
public class WritebackWorkerTests {

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Records() =>
        Arg.Any<IReadOnlyList<IReadOnlyDictionary<string, object?>>>();

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> SentRecords(WritebackHarness harness, string method) =>
        harness.Salesforce.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == method)
            .SelectMany(c => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)c.GetArguments()[method == "UpsertAsync" ? 2 : 1]!)
            .ToList();

    [Fact]
    public async Task AnUpdatedRowWithASalesforceId_SendsOnlyTheChangedFields_ToThatRecord() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(Update(
            before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "111"), ("industry", "Retail")),
            after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222"), ("industry", "Retail")))));

        var record = Assert.Single(SentRecords(harness, "UpdateAsync"));
        Assert.Equal(new Dictionary<string, object?> {
            ["Id"] = "001000000000001AAA",
            ["Phone"] = "222",
            ["Row_Key__c"] = "7"
        }, record);
        await harness.Salesforce.DidNotReceive().UpsertAsync(default!, default!, Records(), default);
    }

    [Fact]
    public async Task AChangeToAColumnWritebackDoesNotCarry_SendsNothing_ButIsStillConfirmed() {
        var harness = new WritebackHarness();

        // name is Inbound only, so absent from the plan's fields; sf_id is the Key Mapping.
        await harness.RunUntilConfirmed(Transaction(Update(
            before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("name", "Acme"), ("phone", "111")),
            after: Row(("id", 7), ("sf_id", "001000000000002AAA"), ("name", "Acme Ltd"), ("phone", "111")))));

        Assert.Empty(harness.Salesforce.ReceivedCalls());
    }

    [Fact]
    public async Task ANewRow_IsUpsertedOnTheWritebackKey_AndTheNewRecordsIdIsStoredThroughTheTaggedRepository() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(Insert(
            Row(("id", 7), ("sf_id", null), ("phone", "222"), ("industry", null), ("revenue", 1250.5m)))));

        var call = Assert.Single(harness.Salesforce.ReceivedCalls());
        Assert.Equal("UpsertAsync", call.GetMethodInfo().Name);
        Assert.Equal("Account", call.GetArguments()[0]);
        Assert.Equal(ExternalIdField, call.GetArguments()[1]);
        var record = Assert.Single((IReadOnlyList<IReadOnlyDictionary<string, object?>>)call.GetArguments()[2]!);
        // Nulls are left out of a create, so Salesforce applies its own defaults.
        Assert.Equal(new Dictionary<string, object?> {
            ["Phone"] = "222",
            ["AnnualRevenue"] = 1250.5m,
            ["Row_Key__c"] = "7"
        }, record);

        await harness.TaggedTarget.Received(1).UpdateByKey("salesforce.account", KeyColumn, 7,
            Arg.Is<Dictionary<string, object?>>(d => d.Count == 1 && (string?)d[KeyMappingColumn] == "001NEW000000000000"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARowInsertedAlreadyHoldingASalesforceId_UpdatesThatRecordInsteadOfCreatingOne() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(Insert(
            Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222"), ("industry", null)))));

        var record = Assert.Single(SentRecords(harness, "UpdateAsync"));
        Assert.Equal("001000000000001AAA", record["Id"]);
        Assert.Equal("222", record["Phone"]);
        // Empty columns are left out: sending a null for each would wipe those fields, and Salesforce refuses some
        // fields on some records even as null (Person Account fields such as FirstName on a business Account).
        Assert.False(record.ContainsKey("Industry"));
        Assert.Empty(SentRecords(harness, "UpsertAsync"));
        await harness.TaggedTarget.DidNotReceiveWithAnyArgs().UpdateByKey(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task AnUpdatedRowThatNeverReachedSalesforce_IsUpsertedWhole_BecauseTheRecordMayNotExist() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(Update(
            before: Row(("id", 7), ("sf_id", null), ("phone", "111"), ("industry", "Retail")),
            after: Row(("id", 7), ("sf_id", null), ("phone", "222"), ("industry", "Retail")))));

        var record = Assert.Single(SentRecords(harness, "UpsertAsync"));
        Assert.Equal("222", record["Phone"]);
        Assert.Equal("Retail", record["Industry"]);
    }

    [Fact]
    public async Task AWritebackDefault_FillsARequiredFieldOnlyWhenTheRowSuppliesNone() {
        var harness = new WritebackHarness();
        harness.Plan = harness.PlanWith(Binding(defaults: [
            new WritebackDefaultValue("FirstName", "Unknown"),
            new WritebackDefaultValue("Rating", "Warm")
        ]));

        await harness.RunUntilConfirmed(Transaction(Insert(
            Row(("id", 7), ("sf_id", null), ("first_name", "Ada")))));

        var record = Assert.Single(SentRecords(harness, "UpsertAsync"));
        Assert.Equal("Ada", record["FirstName"]);
        Assert.Equal("Warm", record["Rating"]);
    }

    [Fact]
    public async Task ADeletedRow_IsNotWrittenBack_SoAMistakenDeleteCannotWipeCrmData() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(Delete(Row(("id", 7), ("sf_id", "001000000000001AAA")))));

        Assert.Empty(harness.Salesforce.ReceivedCalls());
    }

    [Fact]
    public async Task ManyRowsDeletedTogether_AreReportedInOneLogLine_NotOnePerRow() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(
            Delete(Row(("id", 7))), Delete(Row(("id", 8))), Delete(Row(("id", 9)))));

        var line = Assert.Single(harness.ProcessorLog.Entries, e => e.Message.Contains("deleted"));
        Assert.StartsWith("3 row(s) deleted from salesforce.account", line.Message);
    }

    [Fact]
    public async Task ARecordSalesforceRefuses_IsRecordedAsAWritebackFailure_AndThePositionStillMovesOn() {
        var harness = new WritebackHarness();
        harness.Salesforce.UpdateAsync(default!, default!, default).ReturnsForAnyArgs([
            new RecordSaveResult {
                Success = false,
                Errors = [new RecordError { StatusCode = "FIELD_CUSTOM_VALIDATION_EXCEPTION", Message = "Phone must be E.164", Fields = ["Phone"] }]
            }
        ]);

        var transaction = Transaction(Update(
            before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "111")),
            after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "not a phone"))));
        await harness.RunUntilConfirmed(transaction);

        await harness.Failures.Received(1).RecordAsync(Arg.Is<WritebackFailure>(f =>
            f.BindingId == BindingId &&
            f.RowKey == "key:7" &&
            f.SalesforceId == "001000000000001AAA" &&
            f.Operation == WritebackOperation.Update &&
            f.StatusCodes == "FIELD_CUSTOM_VALIDATION_EXCEPTION" &&
            f.Message.Contains("Phone must be E.164") &&
            f.ValuesSent.Contains("not a phone")), Arg.Any<CancellationToken>());
        Assert.Contains(transaction.Position, harness.Capability.Confirmed);
    }

    [Fact]
    public async Task AFailureThatMayPass_ConfirmsNothing_AndSendsTheSameChangesAgain() {
        var harness = new WritebackHarness();
        var attempts = 0;
        harness.Salesforce.UpdateAsync(default!, default!, default).ReturnsForAnyArgs(call => {
            if (Interlocked.Increment(ref attempts) == 1) {
                throw new SalesforceTransientException("Salesforce answered 503");
            }
            return Succeed(call.ArgAt<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(1));
        });

        var transaction = Transaction(Update(
            before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "111")),
            after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222"))));
        await harness.RunUntilConfirmed(transaction);

        Assert.Equal(2, attempts);
        Assert.Equal([transaction.Position], harness.Capability.Confirmed);
        Assert.Equal(2, harness.Capability.StreamsOpened);
        await harness.Failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task ALockedRow_IsRetriedLikeAnOutage_NotRecordedAsAFailure() {
        var harness = new WritebackHarness();
        var attempts = 0;
        harness.Salesforce.UpdateAsync(default!, default!, default).ReturnsForAnyArgs(call =>
            Interlocked.Increment(ref attempts) == 1
                ? Task.FromResult<IReadOnlyList<RecordSaveResult>>([
                    new RecordSaveResult { Success = false, Errors = [new RecordError { StatusCode = "UNABLE_TO_LOCK_ROW", Message = "locked" }] }
                ])
                : Succeed(call.ArgAt<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(1)));

        await harness.RunUntilConfirmed(Transaction(Update(
            before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "111")),
            after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222")))));

        Assert.Equal(2, attempts);
        await harness.Failures.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task TwoChangesToTheSameRow_ReachSalesforceInCommitOrder_InSeparateCalls() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(
            Update(before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "111")),
                after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222"))),
            Update(before: Row(("id", 8), ("sf_id", "001000000000002AAA"), ("phone", "111")),
                after: Row(("id", 8), ("sf_id", "001000000000002AAA"), ("phone", "333"))),
            Update(before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222")),
                after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "444")))));

        var calls = harness.Salesforce.ReceivedCalls()
            .Select(c => ((IReadOnlyList<IReadOnlyDictionary<string, object?>>)c.GetArguments()[1]!)
                .Select(r => $"{r["Id"]}={r["Phone"]}").ToList())
            .ToList();
        Assert.Equal([
            ["001000000000001AAA=222", "001000000000002AAA=333"],
            ["001000000000001AAA=444"]
        ], calls);
    }

    [Fact]
    public async Task ASuccessfulWriteback_ClearsThatRowsOpenFailure() {
        var harness = new WritebackHarness();

        await harness.RunUntilConfirmed(Transaction(Update(
            before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "111")),
            after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222")))));

        await harness.Failures.Received(1).ClearAsync(BindingId, "key:7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValuesAreSentInTheFormSalesforceExpectsForTheFieldsType() {
        var harness = new WritebackHarness();
        harness.Plan = harness.PlanWith(new WritebackBindingPlan {
            BindingId = BindingId, EntityName = "AccountChangeEvent", SourceObject = "Account", Table = Table,
            TableName = "salesforce.account", KeyMappingColumn = KeyMappingColumn,
            WritebackKey = new WritebackField(KeyColumn, ExternalIdField, "string"),
            Fields = [
                new WritebackField("signed_on", "SignedOn__c", "date"),
                new WritebackField("seen_at", "SeenAt__c", "datetime"),
                new WritebackField("opens_at", "OpensAt__c", "time"),
                new WritebackField("employees", "NumberOfEmployees", "int"),
                new WritebackField("vip", "Vip__c", "boolean")
            ]
        });

        await harness.RunUntilConfirmed(Transaction(Insert(Row(
            ("id", 7), ("sf_id", "001000000000001AAA"),
            ("signed_on", new DateTime(2026, 3, 4)),
            ("seen_at", new DateTime(2026, 3, 4, 13, 5, 6, 789, DateTimeKind.Utc)),
            ("opens_at", new TimeSpan(0, 9, 30, 0)),
            ("employees", 120m),
            ("vip", true)))));

        var record = Assert.Single(SentRecords(harness, "UpdateAsync"));
        Assert.Equal("2026-03-04", record["SignedOn__c"]);
        Assert.Equal("2026-03-04T13:05:06.789Z", record["SeenAt__c"]);
        Assert.Equal("09:30:00.000Z", record["OpensAt__c"]);
        Assert.Equal(120L, record["NumberOfEmployees"]);
        Assert.Equal(true, record["Vip__c"]);
    }

    [Fact]
    public async Task StartingUp_TagsEveryWriteBeforeTheDatabaseStartsKeepingChanges_ThenArmsExactlyTheTwoWayTables() {
        var harness = new WritebackHarness();
        harness.TargetConnections.When(t => t.SetWritebackArmedAsync(Arg.Is<DateTime?>(at => at != null), Arg.Any<CancellationToken>()))
            .Do(_ => harness.Capability.Log.Add("armed flag"));

        await harness.RunUntilConfirmed(Transaction(Delete(Row(("id", 7)))));

        // Writes made between the slot appearing and the tag being switched on would be echoed to Salesforce.
        Assert.Equal(["tag", "armed flag", "arm"], harness.Capability.Log);
        Assert.Equal([Table], Assert.Single(harness.Capability.Armed));
    }

    [Fact]
    public async Task WithNoTwoWayBindingLeft_TheDatabaseIsSetBack_SoItStopsKeepingChangesForWriteback() {
        var harness = new WritebackHarness();
        harness.Plan = new WritebackPlan { Capability = harness.Capability, Endpoints = Endpoints };
        harness.Target.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TargetConnection { WritebackArmedAt = DateTime.UtcNow });

        var disarmed = new TaskCompletionSource();
        harness.TargetConnections.When(t => t.SetWritebackArmedAsync(null, Arg.Any<CancellationToken>()))
            .Do(_ => disarmed.TrySetResult());

        await harness.RunUntil(() => disarmed.Task);

        Assert.Equal(1, harness.Capability.Disarmed);
        Assert.Empty(harness.Capability.Armed);
    }

    #region Incidents

    [Fact]
    public async Task AFailureThatMayPass_IsAWritebackIncident_ThatEndsOnceWritebackRunsAgain() {
        var harness = new WritebackHarness();
        var attempts = 0;
        harness.Salesforce.UpdateAsync(default!, default!, default).ReturnsForAnyArgs(call => {
            if (Interlocked.Increment(ref attempts) == 1) {
                throw new SalesforceTransientException("Salesforce answered 503");
            }
            return Succeed(call.ArgAt<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(1));
        });

        await harness.RunUntilConfirmed(Transaction(Update(
            before: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "111")),
            after: Row(("id", 7), ("sf_id", "001000000000001AAA"), ("phone", "222")))));

        var incident = Assert.Single(harness.IncidentStore.All);
        Assert.Equal(IncidentKind.Retrying, incident.Kind);
        Assert.Equal(RunDirection.Writeback, incident.Direction);
        Assert.Contains("503", incident.Message);
        Assert.NotNull(incident.EndedAt);
    }

    [Fact]
    public async Task BecomingBlockedWhileRunning_IsAWritebackIncident() {
        var harness = new WritebackHarness();
        var running = harness.RunUntil(async () => {
            while (harness.Health.State != Application.Runtime.RunState.Running) {
                await Task.Delay(5);
            }
            harness.Plan = new WritebackPlan { Bindings = harness.Plan.Bindings, BlockedReason = "The Replication Credential was refused." };
            harness.Signal.Signal();
            while (harness.IncidentStore.All.Count == 0) {
                await Task.Delay(5);
            }
        });
        await running;

        var incident = Assert.Single(harness.IncidentStore.All);
        Assert.Equal(IncidentKind.Blocked, incident.Kind);
        Assert.Equal(RunDirection.Writeback, incident.Direction);
        Assert.Contains("Replication Credential", incident.Message);
    }

    #endregion
}
