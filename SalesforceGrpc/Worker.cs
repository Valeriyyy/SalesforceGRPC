using Avro;
using Grpc.Core;
using GrpcClient;
using MediatR;
using Microsoft.Extensions.Options;
using SalesforceGrpc.Salesforce;
using SqlKata.Execution;
using SalesforceGrpc.Handlers;
using SalesforceGrpc.Extensions;
using Database;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Humanizer.Bytes;
//using Newtonsoft.Json;

namespace SalesforceGrpc;

public class Worker : BackgroundService {
    private readonly ILogger<Worker> _logger;
    private readonly SalesforceConfig _sfconfig;
    private readonly PubSub.PubSubClient _pubsubClient;
    //private readonly SalesforceAvroDeserializer _processor;
    private readonly IConfiguration _config;
    private readonly QueryFactory _db;
    private readonly IMediator _mediator;
    //public static string token = "00D2F000000F5Dn!AQkAQH8rkJV3nKzxr2IBTafD11srDTHWfCa9SfcBMgBvyyQX4rP8d7UL4Yjinpenbe7hdae40OlcW6sAMuJYPO18hX2XHOMO";

    public Worker(
        ILogger<Worker> logger,
        IOptions<SalesforceConfig> sfconfig,
        //SalesforceAvroDeserializer processor,
        IConfiguration config,
        QueryFactory db,
        PubSub.PubSubClient psClient,
        IMediator mediator
        ) {
        _logger = logger;
        _sfconfig = sfconfig.Value;
        //_processor = processor;
        _pubsubClient = psClient;
        _config = config;
        _db = db;
        _mediator = mediator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            //await TestMethod();
            await ListenForChannelEvents(stoppingToken);
            /*await GetAndSaveSchema("AccountChangeEvent");
            await GetAndSaveSchema("ContactChangeEvent");
            await GetAndSaveSchema("Some_Custom_Object__ChangeEvent");*/
        } catch (RpcException exc) {
            _logger.LogCritical("RCPException thrown with message: {message}", exc.Message);
            _logger.LogCritical("Status: {status}", exc.StatusCode);
        }
    }

    private async Task ListenForChannelEvents(CancellationToken stoppingToken) {
        var fetchRequest = new FetchRequest {
            TopicName = "/data/MyCustomChannel__chn",
            NumRequested = 25
        };
        using var stream = _pubsubClient.Subscribe(null, null, stoppingToken);
        await stream.RequestStream.WriteAsync(fetchRequest, stoppingToken);

        var schemaDict = (await _db
            .Query("salesforce.cdc_schemas")
            .Select("id as Id", "schema_id as SchemaId", "schema_name as SchemaName")
            .GetAsync<CDCSchema>(null, null, stoppingToken))
            .ToDictionary(p => p.SchemaId, p => p.SchemaName);

        while (await stream.ResponseStream.MoveNext(stoppingToken)) {
            var latestReplayId = stream.ResponseStream.Current.LatestReplayId.ToLongBE();
            _logger.LogInformation("latest Replay Id: {replayId}", latestReplayId);
            _logger.LogInformation("Time: {rightNow} RPC ID: {RpcId}", DateTime.Now, stream.ResponseStream.Current.RpcId);

            var re = stream.ResponseStream.Current;
            Console.WriteLine("number of events in payload " + re.Events.Count);
            //await File.WriteAllTextAsync("Events.json", JsonConvert.SerializeObject(re));
            if (re?.Events is not null) {
                var eventTasks = new List<Task>();
                for (int i = 0; i < re.Events.Count; i++) {
                    var e = re.Events[i];
                    var eventReplayId = e.ReplayId;
                    var replayId = eventReplayId.ToLongBE();
                    _logger.LogInformation("Event Replay Id: {replayId}", replayId);
                    Console.WriteLine("event number " + i);
                    var payload = e.Event.Payload;
                    _logger.LogInformation("Schema Id: {schemaId}", e.Event.SchemaId);
                    var validSchemaId = schemaDict.TryGetValue(e.Event.SchemaId, out var eventType);
                    if (!validSchemaId) {
                        throw new Exception("Unrecognized Schema Id: " + e.Event.SchemaId);
                    }
                    if (eventType == "AccountChangeEvent") {
                        Console.WriteLine("Account Event");
                        var accSchema = Schema.Parse(File.ReadAllText("./avro/AccountChangeEventGRPCSchema.avsc"));
                        eventTasks.Add(_mediator.Send(new GenericCDCEventCommand {
                            Name = "Account Change Event",
                            AvroPayload = payload.ToByteArray(),
                            AvroSchema = accSchema
                        }, stoppingToken));
                    } else if (eventType == "ContactChangeEvent") {
                        Console.WriteLine("Contact Event");
                        var contSchema = Schema.Parse(File.ReadAllText("./avro/ContactChangeEventGRPCSchema.avsc"));
                        eventTasks.Add(_mediator.Send(new GenericCDCEventCommand {
                            Name = "Contact Change Event",
                            AvroPayload = payload.ToByteArray(),
                            AvroSchema = contSchema
                        }, stoppingToken));
                    }
                }
                await Task.WhenAll(eventTasks);
            }
        }
    }

    private async Task PublishEvent(CancellationToken stoppingToken) {
        /*var tokenString = token;
        var headers = new Metadata
        {
            { "accesstoken", $"Bearer {tokenString}" },
            { "instanceurl", _sfconfig.OrgUrl },
            { "tenantid", _sfconfig.OrgId }
        };*/
        var dict = new Dictionary<string, string> {
            { "a", "1" },
            { "b", "2" },
            { "c", "3" }
        };
        var events = new ProducerEvent[] {
            new ProducerEvent {
                Id = "123123", SchemaId = "lhKgvxi31DtlAz18-TdwBQ", Payload = null
            }
        };
        var request = new PublishRequest {
            TopicName = "/data/AccountChangeEvent"
        };
        var res = await _pubsubClient.PublishAsync(request, null, null, stoppingToken);
        _logger.LogInformation(res.ToString());
    }

    public async Task GetAndSaveSchema(string schemaName) {
        var topicRequest = new TopicRequest {
            TopicName = $"/data/{schemaName}"
        };
        var topicResponse = await _pubsubClient.GetTopicAsync(topicRequest);
        Console.WriteLine("THIS IS SCHEMA ID " + topicResponse.SchemaId);
        var schemaRequest = new SchemaRequest {
            SchemaId = topicResponse.SchemaId
        };
        var someSchema = await _pubsubClient.GetSchemaAsync(schemaRequest);
        var avroSchema = Schema.Parse(someSchema.SchemaJson);
        var name = $"{avroSchema.Name}GRPCSchema.avsc";
        Console.WriteLine("saving schame as " + name);
        File.WriteAllText($"./avro/{name}", someSchema.SchemaJson);
    }


    public async Task TestMethod() {
        var eventsJson = File.ReadAllText("./AccountUpdateEvents.json");
        var events = JsonSerializer.Deserialize<List<EventWrapper>>(eventsJson) ?? throw new NullReferenceException("Eventrs are null");
        Console.WriteLine("Number of events " + events.Count);
        var eventTasks = new List<Task>();
        var accSchema = Schema.Parse(File.ReadAllText("./avro/AccountChangeEventGRPCSchema.avsc"));
        foreach (var eve in events) {
            Console.WriteLine("event " + eve.ReplayId);
            // Console.WriteLine(eve.Event.Payload);
            var byteString = ByteString.CopyFromUtf8(eve.Event.Payload);
            var replayId = (ByteString.CopyFromUtf8(eve.ReplayId)).ToLongBE();
            Console.WriteLine(replayId);
            Console.WriteLine(byteString.Length);
            eventTasks.Add(_mediator.Send(new GenericCDCEventCommand {
                Name = "Account Change Event",
                AvroPayload = byteString.ToByteArray(),
                AvroSchema = accSchema
            }));
        }

        await Task.WhenAll(eventTasks);
    }

    public class EventWrapper {
        [JsonPropertyName("event")]
        public CDCEvent Event { get; set; }
        [JsonPropertyName("replayId")]
        public string ReplayId { get; set; }
    }

    public class CDCEvent {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("schemaId")]
        public string SchemaId { get; set; }
        [JsonPropertyName("payload")]
        public string Payload { get; set; }
    }
}