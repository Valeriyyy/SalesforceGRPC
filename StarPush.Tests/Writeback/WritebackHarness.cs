using Application.Bindings;
using Application.Services.Interfaces;
using Application.Targets;
using Application.Writeback;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Writeback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Salesforce.Writeback;
using StarPush.Tests.Worker;
using StarPush.Writeback;
using System.Threading.Channels;

namespace StarPush.Tests.Writeback;

/// <summary>
/// Runs the real <see cref="WritebackWorker"/> and <see cref="WritebackProcessor"/> over a fake engine
/// capability that streams scripted Target Table changes, a substituted Salesforce writer, a substituted
/// failure store and a substituted (tagged) Target Database.
/// </summary>
internal sealed class WritebackHarness {
    public const int BindingId = 42;
    public static readonly WritebackTable Table = new("salesforce", "account");
    public const string KeyMappingColumn = "sf_id";
    public const string KeyColumn = "id";
    public const string ExternalIdField = "Row_Key__c";

    public FakeWritebackCapability Capability { get; } = new();
    public ISalesforceRecordWriter Salesforce { get; } = Substitute.For<ISalesforceRecordWriter>();
    public IWritebackFailureRepository Failures { get; } = Substitute.For<IWritebackFailureRepository>();
    public IRepository TaggedTarget { get; } = Substitute.For<IRepository>();
    public IBindingService Bindings { get; } = Substitute.For<IBindingService>();
    public ITargetConnectionRepository TargetConnections { get; } = Substitute.For<ITargetConnectionRepository>();
    public ConfigurationChangeSignal Signal { get; } = new();
    // The real clock: the worker's pauses are timers, and the retry delay is shortened below.
    public TimeProvider Time { get; } = TimeProvider.System;
    public ITargetConnectionProvider Target { get; }
    public ListLogger<WritebackWorker> Log { get; } = new();
    public WritebackHealth Health { get; } = new();

    public static readonly WritebackEndpoints Endpoints = new("Host=db;Username=app", "Host=db;Username=repl");

    public WritebackPlan Plan { get; set; }

    public WritebackHarness() {
        Target = TargetProviders.Of(TaggedTarget);
        Plan = PlanWith(Binding());
        Bindings.GetWritebackPlanAsync(Arg.Any<CancellationToken>()).Returns(_ => Plan);

        // Every record succeeds unless a test says otherwise; an upsert reports a created record.
        Salesforce.UpdateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Succeed(call.ArgAt<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(1)));
        Salesforce.UpsertAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Succeed(call.ArgAt<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(2), created: true));
    }

    public WritebackPlan PlanWith(params WritebackBindingPlan[] bindings) => new() {
        Bindings = bindings,
        Capability = Capability,
        Endpoints = Endpoints
    };

    /// <summary>
    /// An Account Binding: Phone and Industry written back, Name inbound only (so absent here), and a Writeback
    /// Key from the table's id column to Row_Key__c.
    /// </summary>
    public static WritebackBindingPlan Binding(IReadOnlyList<WritebackDefaultValue>? defaults = null) => new() {
        BindingId = BindingId,
        EntityName = "AccountChangeEvent",
        SourceObject = "Account",
        Table = Table,
        TableName = "salesforce.account",
        KeyMappingColumn = KeyMappingColumn,
        WritebackKey = new WritebackField(KeyColumn, ExternalIdField, "string"),
        Fields = [
            new WritebackField("phone", "Phone", "phone"),
            new WritebackField("industry", "Industry", "picklist"),
            new WritebackField("revenue", "AnnualRevenue", "currency"),
            new WritebackField("first_name", "FirstName", "string")
        ],
        Defaults = defaults ?? []
    };

    public static Task<IReadOnlyList<RecordSaveResult>> Succeed(
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? records, bool created = false) =>
        // Null while a test re-stubs the substitute, which calls the stub being replaced.
        Task.FromResult<IReadOnlyList<RecordSaveResult>>((records ?? []).Select((r, i) => new RecordSaveResult {
            Success = true,
            Created = created,
            Id = r.TryGetValue("Id", out var id) && id is string existing ? existing : $"001NEW{i:D12}"
        }).ToList());

    private WritebackWorker Build() {
        var services = new ServiceCollection();
        services.AddSingleton(Bindings);
        services.AddSingleton(new WritebackProcessor(Salesforce, Failures, Time, NullLogger<WritebackProcessor>.Instance));
        var provider = services.BuildServiceProvider();

        return new WritebackWorker(Log, provider.GetRequiredService<IServiceScopeFactory>(), Signal, Target,
            TargetConnections, Health, Time) {
            RetryDelay = TimeSpan.FromMilliseconds(20)
        };
    }

    /// <summary>Starts the worker, waits for <paramref name="until"/>, then stops it.</summary>
    public async Task RunUntil(Func<Task> until) {
        var worker = Build();
        await worker.StartAsync(CancellationToken.None);
        try {
            await until().WaitAsync(TimeSpan.FromSeconds(10));
        } catch (TimeoutException) {
            throw new TimeoutException("The Writeback worker never reached the expected point. Its log:\n" +
                string.Join("\n", Log.Entries.Select(e => $"{e.Level}: {e.Message}")));
        } finally {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Scripts these transactions, then runs until every one of them has been confirmed.</summary>
    public Task RunUntilConfirmed(params ChangeTransaction[] transactions) {
        foreach (var transaction in transactions) {
            Capability.Enqueue(transaction);
        }
        var last = transactions[^1].Position;
        return RunUntil(() => Capability.WaitForConfirm(last));
    }

    #region Scripting changes

    private static ulong _position;

    public static ChangeTransaction Transaction(params RowChange[] changes) =>
        new() { Changes = changes, Position = new WritebackPosition(Interlocked.Increment(ref _position)) };

    public static Dictionary<string, object?> Row(params (string Column, object? Value)[] values) =>
        values.ToDictionary(v => v.Column, v => v.Value);

    public static RowChange Insert(Dictionary<string, object?> row) =>
        new() { Table = Table, Kind = RowChangeKind.Insert, NewValues = row };

    public static RowChange Update(Dictionary<string, object?> before, Dictionary<string, object?> after) =>
        new() { Table = Table, Kind = RowChangeKind.Update, OldValues = before, NewValues = after };

    public static RowChange Delete(Dictionary<string, object?> before) =>
        new() { Table = Table, Kind = RowChangeKind.Delete, OldValues = before, NewValues = new Dictionary<string, object?>() };

    #endregion
}

/// <summary>
/// An engine capability whose change stream replays scripted transactions and records what is confirmed,
/// armed and disarmed.
/// </summary>
internal sealed class FakeWritebackCapability : IWritebackCapability {
    private readonly Channel<ChangeTransaction> _pending = Channel.CreateUnbounded<ChangeTransaction>();
    private readonly List<ChangeTransaction> _unconfirmed = [];
    private readonly object _lock = new();
    private readonly List<(WritebackPosition Position, TaskCompletionSource Done)> _confirmWaiters = [];

    public List<WritebackPosition> Confirmed { get; } = [];
    public List<IReadOnlyList<WritebackTable>> Armed { get; } = [];
    public int Disarmed { get; private set; }
    public int StreamsOpened { get; private set; }
    public List<WritebackPrerequisite> Prerequisites { get; } = [];

    public void Enqueue(ChangeTransaction transaction) => _pending.Writer.TryWrite(transaction);

    public Task WaitForConfirm(WritebackPosition position) {
        lock (_lock) {
            if (Confirmed.Contains(position)) {
                return Task.CompletedTask;
            }
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmWaiters.Add((position, done));
            return done.Task;
        }
    }

    public Task<string?> GetUnavailableReasonAsync(string connectionString, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<IReadOnlyList<WritebackPrerequisite>> CheckPrerequisitesAsync(WritebackEndpoints endpoints,
        IReadOnlyList<WritebackTable> tables, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WritebackPrerequisite>>(Prerequisites);

    public Task ArmAsync(WritebackEndpoints endpoints, IReadOnlyList<WritebackTable> tables,
        CancellationToken cancellationToken = default) {
        lock (_lock) {
            Armed.Add(tables);
        }
        return Task.CompletedTask;
    }

    public Task DisarmAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default) {
        lock (_lock) {
            Disarmed++;
        }
        return Task.CompletedTask;
    }

    public Task<IWritebackChangeStream> OpenStreamAsync(WritebackEndpoints endpoints,
        CancellationToken cancellationToken = default) {
        lock (_lock) {
            StreamsOpened++;
            // A reopened stream resends everything not yet confirmed, as a replication slot does.
            foreach (var unconfirmed in _unconfirmed.ToList()) {
                _pending.Writer.TryWrite(unconfirmed);
            }
            _unconfirmed.Clear();
        }
        return Task.FromResult<IWritebackChangeStream>(new Stream(this));
    }

    public IRepository CreateTaggedRepository(string connectionString) => throw new NotSupportedException();

    public Task<long?> GetLagBytesAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(0);

    public Task<string?> ProveReplicationCredentialAsync(string replicationConnectionString,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    private sealed class Stream(FakeWritebackCapability owner) : IWritebackChangeStream {
        public async IAsyncEnumerable<ChangeTransaction> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            while (await owner._pending.Reader.WaitToReadAsync(cancellationToken)) {
                while (owner._pending.Reader.TryRead(out var transaction)) {
                    lock (owner._lock) {
                        owner._unconfirmed.Add(transaction);
                    }
                    yield return transaction;
                }
            }
        }

        public Task ConfirmAsync(WritebackPosition position, CancellationToken cancellationToken = default) {
            lock (owner._lock) {
                owner.Confirmed.Add(position);
                owner._unconfirmed.RemoveAll(t => t.Position.Value <= position.Value);
                foreach (var waiter in owner._confirmWaiters.Where(w => w.Position.Value <= position.Value).ToList()) {
                    waiter.Done.TrySetResult();
                    owner._confirmWaiters.Remove(waiter);
                }
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
