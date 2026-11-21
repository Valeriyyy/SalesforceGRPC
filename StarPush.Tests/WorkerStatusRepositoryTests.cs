using Dapper;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace StarPush.Tests;

/// <summary>
/// Integration tests for the Inbound Failure and Incident stores, against a real Postgres.
/// </summary>
/// <remarks>
/// The connection string comes from <c>STARPUSH_TEST_APP_DATABASE</c>, and every test skips when it is unset.
/// <b>These tests delete rows</b>: every Inbound Failure and Incident, and the Bindings they create. Point the
/// variable at a throwaway App Database with migrations 001–008 applied, never at a real one.
/// </remarks>
[Collection(AppDatabaseCollection.Name)]
public class WorkerStatusRepositoryTests {
    private const string ConnectionStringVariable = "STARPUSH_TEST_APP_DATABASE";
    private const string Entity = "WorkerStatusTestChangeEvent";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ConnectionString() {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
            $"Set {ConnectionStringVariable} to a throwaway App Database to run this test. It deletes what is there.");
        return connectionString!;
    }

    private static IConfiguration Configuration(string connectionString) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:appDatabase"] = connectionString })
        .Build();

    /// <summary>A clean slate with one Binding to hang failures on; returns its id.</summary>
    private static async Task<int> ResetWithBinding(string connectionString) {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync("""
            DELETE FROM salesforce.inbound_failures;
            DELETE FROM salesforce.incidents;
            DELETE FROM salesforce.mapped_fields WHERE schema_id IN (SELECT id FROM salesforce.cdc_schemas WHERE entity_name = @Entity);
            DELETE FROM salesforce.cdc_schemas WHERE entity_name = @Entity;
            """, new { Entity });
        return await connection.ExecuteScalarAsync<int>("""
            INSERT INTO salesforce.cdc_schemas (entity_name, db_schema_full_name, binding_state)
            VALUES (@Entity, 'salesforce.worker_status_test', 'Active') RETURNING id
            """, new { Entity });
    }

    private static InboundFailure Failure(int bindingId, byte replay, DateTime at, string message = "mapping is corrupt",
        string? changeType = "UPDATE", string[]? recordIds = null) => new() {
        BindingId = bindingId, ChannelFullName = "Sales__chn", ReplayId = [0, 0, 0, replay], EntityName = Entity,
        ChangeType = changeType, RecordIds = recordIds ?? ["001A", "001B"], Message = message,
        FirstFailedAt = at, LastFailedAt = at
    };

    [Fact]
    public async Task AnInboundFailure_RecordedAgainForTheSameEvent_IsOneFailure_CountingTheAttempt() {
        var connectionString = ConnectionString();
        var bindingId = await ResetWithBinding(connectionString);
        var failures = new InboundFailureRepository(Configuration(connectionString));
        var first = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

        await failures.RecordAsync(Failure(bindingId, 1, first), Ct);
        await failures.RecordAsync(Failure(bindingId, 1, first.AddMinutes(5), "still corrupt"), Ct);
        await failures.RecordAsync(Failure(bindingId, 2, first.AddMinutes(6), changeType: null, recordIds: []), Ct);

        var all = await failures.ListAsync(bindingId, Ct);
        Assert.Equal(2, all.Count);
        var replayed = Assert.Single(all, f => f.ReplayId.SequenceEqual(new byte[] { 0, 0, 0, 1 }));
        Assert.Equal(2, replayed.Attempts);
        Assert.Equal("still corrupt", replayed.Message);
        Assert.Equal(first, replayed.FirstFailedAt.ToUniversalTime());
        Assert.Equal(["001A", "001B"], replayed.RecordIds);
        var unreadable = Assert.Single(all, f => f.ReplayId.SequenceEqual(new byte[] { 0, 0, 0, 2 }));
        Assert.Null(unreadable.ChangeType);
        Assert.Empty(unreadable.RecordIds);
        Assert.Equal(2, await failures.CountAsync(Ct));
        Assert.Equal(new Dictionary<int, int> { [bindingId] = 2 }, await failures.CountByBindingAsync(Ct));
    }

    [Fact]
    public async Task DismissingAnInboundFailure_RemovesIt_AndDeletingItsBindingRemovesTheRest() {
        var connectionString = ConnectionString();
        var bindingId = await ResetWithBinding(connectionString);
        var failures = new InboundFailureRepository(Configuration(connectionString));
        var at = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
        await failures.RecordAsync(Failure(bindingId, 1, at), Ct);
        await failures.RecordAsync(Failure(bindingId, 2, at), Ct);

        await failures.DeleteAsync((await failures.ListAsync(null, Ct))[0].Id, Ct);
        Assert.Equal(1, await failures.CountAsync(Ct));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync("DELETE FROM salesforce.cdc_schemas WHERE id = @Id", new { Id = bindingId });
        Assert.Equal(0, await failures.CountAsync(Ct));
    }

    [Fact]
    public async Task Incidents_OpenContinueAndClose_AndOnlyTheLast24HoursAreKept() {
        var connectionString = ConnectionString();
        await ResetWithBinding(connectionString);
        var incidents = new IncidentRepository(Configuration(connectionString));
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        var old = await incidents.OpenAsync(new Incident {
            Kind = IncidentKind.Started, Message = "old start", StartedAt = now.AddHours(-30), EndedAt = now.AddHours(-30)
        }, Ct);
        var ongoingForDays = await incidents.OpenAsync(new Incident {
            Direction = RunDirection.Inbound, Kind = IncidentKind.Blocked, Message = "Org Connection Failed",
            StartedAt = now.AddHours(-50)
        }, Ct);
        var retrying = await incidents.OpenAsync(new Incident {
            Direction = RunDirection.Writeback, Kind = IncidentKind.Retrying, Message = "Salesforce answered 503", StartedAt = now
        }, Ct);
        await incidents.ContinueAsync(retrying, "Salesforce answered 503 again", Ct);
        await incidents.CloseAsync(retrying, now.AddMinutes(2), Ct);

        var recent = await incidents.ListSinceAsync(now - IIncidentRepository.Retention, Ct);

        Assert.DoesNotContain(recent, i => i.Id == old);
        Assert.Contains(recent, i => i.Id == ongoingForDays && i.EndedAt is null && i.Direction == RunDirection.Inbound);
        var stretch = Assert.Single(recent, i => i.Id == retrying);
        Assert.Equal((2, "Salesforce answered 503 again"), (stretch.Occurrences, stretch.Message));
        Assert.Equal(now.AddMinutes(2), stretch.EndedAt?.ToUniversalTime());
        Assert.Equal(RunDirection.Writeback, stretch.Direction);

        // Opening one prunes those that ended more than 24 hours before it; one still going on is never pruned.
        await using var connection = new NpgsqlConnection(connectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM salesforce.incidents WHERE id = @Id", new { Id = old }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM salesforce.incidents WHERE id = @Id", new { Id = ongoingForDays }));
    }

    [Fact]
    public async Task TheLatestLifecycleIncident_SaysWhetherThePreviousRunStoppedCleanly() {
        var connectionString = ConnectionString();
        await ResetWithBinding(connectionString);
        var incidents = new IncidentRepository(Configuration(connectionString));
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        Assert.Null(await incidents.GetLatestLifecycleKindAsync(Ct));

        await incidents.OpenAsync(new Incident { Kind = IncidentKind.Started, Message = "started", StartedAt = now, EndedAt = now }, Ct);
        Assert.Equal(IncidentKind.Started, await incidents.GetLatestLifecycleKindAsync(Ct));

        await incidents.OpenAsync(new Incident { Kind = IncidentKind.Stopped, Message = "stopped", StartedAt = now.AddMinutes(1), EndedAt = now.AddMinutes(1) }, Ct);
        Assert.Equal(IncidentKind.Stopped, await incidents.GetLatestLifecycleKindAsync(Ct));

        await incidents.CloseAllOpenAsync(now.AddMinutes(2), Ct);
    }
}
