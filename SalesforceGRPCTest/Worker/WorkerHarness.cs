using Application.Bindings;
using Application.Services.Interfaces;
using Application.Targets;
using Avro;
using Avro.Generic;
using Avro.IO;
using Database.Models;
using Database.Repositories.Interfaces;
using Google.Protobuf;
using GrpcClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SalesforceGrpc.Strategies;

namespace SalesforceGRPCTest.Worker;

/// <summary>
/// Runs the real <see cref="SalesforceGrpc.Worker"/> — real strategies, real decoding — against a scripted
/// Pub/Sub server, a substituted Target Database and a substituted Checkpoint store.
/// </summary>
internal sealed class WorkerHarness {
    public const string SchemaId = "SCHEMA_1";
    public const int BindingId = 42;
    public const int ChannelId = 7;
    public const string Table = "salesforce.account";
    public const string KeyColumn = "sf_id";

    /// <summary>
    /// A cut-down AccountChangeEvent: the header every strategy reads, and one mapped field. Phone is Avro
    /// field 1, so an UPDATE of it carries the changed-fields bitmap 0x02.
    /// </summary>
    public const string SchemaJson = """
        {
          "type": "record", "name": "AccountChangeEvent", "namespace": "com.sforce.eventbus",
          "fields": [
            { "name": "ChangeEventHeader", "type": {
                "type": "record", "name": "ChangeEventHeader", "fields": [
                  { "name": "entityName", "type": "string" },
                  { "name": "recordIds", "type": { "type": "array", "items": "string" } },
                  { "name": "changeType", "type": { "type": "enum", "name": "ChangeType",
                      "symbols": ["CREATE", "UPDATE", "DELETE", "UNDELETE"] } },
                  { "name": "changedFields", "type": { "type": "array", "items": "string" } }
                ] } },
            { "name": "Phone", "type": ["null", "string"], "default": null }
          ]
        }
        """;

    private static readonly RecordSchema Schema = (RecordSchema)Avro.Schema.Parse(SchemaJson);

    public FakePubSub PubSub { get; } = new();
    public IMetaRepository Meta { get; } = Substitute.For<IMetaRepository>();
    public IAvroSchemaRepository AvroSchemas { get; } = Substitute.For<IAvroSchemaRepository>();
    public IRepository Target { get; } = Substitute.For<IRepository>();
    public IBindingService Bindings { get; } = Substitute.For<IBindingService>();
    public ITargetConnectionService TargetConnections { get; } = Substitute.For<ITargetConnectionService>();
    public ICheckpointRepository Checkpoints { get; } = Substitute.For<ICheckpointRepository>();
    public ConfigurationChangeSignal Signal { get; } = new();
    public ListLogger<SalesforceGrpc.Worker> Log { get; } = new();

    public SubscriptionPlan Plan { get; set; }

    public WorkerHarness() {
        Plan = new SubscriptionPlan {
            HasConnection = true,
            TargetConnectionState = ConnectionState.Connected,
            TopicName = "/data/Sales__chn",
            ChannelFullName = "Sales__chn",
            ChannelId = ChannelId,
            ActiveBindingsBySchemaId = new() { [SchemaId] = Binding() }
        };

        Bindings.GetSubscriptionPlanAsync(Arg.Any<CancellationToken>()).Returns(_ => Plan);
        Meta.GetCachedMapping(BindingId, Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string> {
            ["MappedSFKey"] = KeyColumn,
            ["Phone"] = "phone"
        });
    }

    public static CDCSchema Binding() => new() {
        Id = BindingId,
        EntityName = "AccountChangeEvent",
        DbSchemaFullName = Table,
        BindingState = BindingState.Active,
        AvroSchema = new DbAvroSchema {
            Id = 1, SchemaId = SchemaId, RecordName = "AccountChangeEvent", SchemaJson = SchemaJson,
            DateCreated = DateTime.UtcNow
        }
    };

    private SalesforceGrpc.Worker Build() {
        var services = new ServiceCollection();
        services.AddSingleton(Bindings);
        services.AddSingleton(TargetConnections);
        var provider = services.BuildServiceProvider();

        var targets = TargetProviders.Of(Target);
        var strategies = new IEventStrategy[] {
            new CreateStrategy(NullLogger<CreateStrategy>.Instance, Meta, targets),
            new UpdateStrategy(NullLogger<UpdateStrategy>.Instance, Meta, targets),
            new DeleteStrategy(NullLogger<DeleteStrategy>.Instance, targets, Meta),
            new UndeleteStrategy(NullLogger<UndeleteStrategy>.Instance, targets, Meta)
        };

        return new SalesforceGrpc.Worker(Log, PubSub, Meta, AvroSchemas, Checkpoints,
            provider.GetRequiredService<IServiceScopeFactory>(), Signal, new EventResolver(strategies));
    }

    /// <summary>Starts the worker, waits for <paramref name="until"/>, then stops it.</summary>
    public async Task RunUntil(Func<Task> until) {
        var worker = Build();
        await worker.StartAsync(CancellationToken.None);
        try {
            await until().WaitAsync(TimeSpan.FromSeconds(10));
        } catch (TimeoutException) {
            // What the worker said is the fastest way to see why it never got there.
            throw new TimeoutException("The worker never reached the expected point. Its log:\n" +
                string.Join("\n", Log.Entries.Select(e => $"{e.Level}: {e.Message}")));
        } finally {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    public Task RunUntil(FakeSubscription subscription) => RunUntil(() => subscription.Drained.Task);

    #region Scripting events

    /// <summary>A response carrying these events, positioned at <paramref name="latestReplayId"/>.</summary>
    public static FetchResponse Response(byte latestReplayId, params ConsumerEvent[] events) {
        var response = new FetchResponse { LatestReplayId = ReplayId(latestReplayId), RpcId = "rpc" };
        response.Events.AddRange(events);
        return response;
    }

    /// <summary>An empty response: the keepalive Salesforce sends while nothing is happening.</summary>
    public static FetchResponse Keepalive(byte latestReplayId) => Response(latestReplayId);

    public static ByteString ReplayId(byte value) => ByteString.CopyFrom(0, 0, 0, 0, 0, 0, 0, value);

    /// <summary>Matches the raw bytes of <see cref="ReplayId"/>, the way they reach the Checkpoint store.</summary>
    public static byte[] IsReplayId(byte value) => Arg.Is<byte[]>(b => b.SequenceEqual(ReplayId(value).ToByteArray()));

    /// <summary>An event whose payload is not Avro at all.</summary>
    public static ConsumerEvent Garbage() => new() {
        ReplayId = ReplayId(0),
        Event = new ProducerEvent { SchemaId = SchemaId, Payload = ByteString.CopyFrom(0xFF, 0xFF, 0xFF) }
    };

    public static ConsumerEvent Create(string recordId, string phone) =>
        Event("CREATE", [recordId], phone, []);

    public static ConsumerEvent Update(string phone, params string[] recordIds) =>
        Event("UPDATE", recordIds, phone, ["0x02"]);

    public static ConsumerEvent Delete(params string[] recordIds) => Event("DELETE", recordIds, null, []);

    private static ConsumerEvent Event(string changeType, string[] recordIds, string? phone, string[] changedFields) {
        var headerSchema = (RecordSchema)Schema["ChangeEventHeader"].Schema;
        var header = new GenericRecord(headerSchema);
        header.Add("entityName", "Account");
        header.Add("recordIds", recordIds.Cast<object>().ToArray());
        header.Add("changeType", new GenericEnum((EnumSchema)headerSchema["changeType"].Schema, changeType));
        header.Add("changedFields", changedFields.Cast<object>().ToArray());

        var record = new GenericRecord(Schema);
        record.Add("ChangeEventHeader", header);
        record.Add("Phone", phone);

        using var stream = new MemoryStream();
        new GenericDatumWriter<GenericRecord>(Schema).Write(record, new BinaryEncoder(stream));

        return new ConsumerEvent {
            ReplayId = ReplayId(0),
            Event = new ProducerEvent { SchemaId = SchemaId, Payload = ByteString.CopyFrom(stream.ToArray()) }
        };
    }

    #endregion
}

/// <summary>Keeps every log entry so a test can assert that a failure was made visible.</summary>
internal sealed class ListLogger<T> : ILogger<T> {
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries {
        get { lock (_entries) { return _entries.ToList(); } }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        lock (_entries) {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>A driver failure that never reached a server — the Target Database is down, not the data bad.</summary>
internal sealed class UnreachableDatabaseException : System.Data.Common.DbException {
    public UnreachableDatabaseException() : base("connection refused") { }
}
