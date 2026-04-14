using Avro;
using Avro.Generic;
using Avro.IO;
using com.sforce.eventbus;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Grpc.Core;
using GrpcClient;
using Microsoft.Extensions.Options;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Salesforce;
using SalesforceGrpc.Strategies;

//using Newtonsoft.Json;

namespace SalesforceGrpc;

public class Worker : BackgroundService {
    private readonly ILogger<Worker> _logger;
    private readonly SalesforceConfig _sfconfig;
    private readonly PubSub.PubSubClient _pubsubClient;
    private readonly SalesforceClient _client;
    private readonly IConfiguration _config;
    private readonly EventResolver _eventResolver;
    
    private readonly IMetaRepository _metaRepo;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public Worker(
        ILogger<Worker> logger,
        IOptions<SalesforceConfig> sfconfig,
        IConfiguration config,
        PubSub.PubSubClient psClient,
        IHostApplicationLifetime hostApplicationLifetime, 
        SalesforceClient client, 
        IMetaRepository metaRepo,
        EventResolver eventResolver) {
        _logger = logger;
        _sfconfig = sfconfig.Value;
        _pubsubClient = psClient;
        _config = config;
        _hostApplicationLifetime = hostApplicationLifetime;
        _client = client;
        _metaRepo = metaRepo;
        _eventResolver = eventResolver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            // while (!stoppingToken.IsCancellationRequested)
            // {
            //     _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            //     await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            // }
            // await TestMethod(stoppingToken);
            // await GetAndSaveSchema("Some_Custom_Object__ChangeEvent");
            await ListenForChannelEventsStrategy(stoppingToken);
            // await GetAndSaveSchema("AccountChangeEvent");
            /*await GetAndSaveSchema("AccountChangeEvent");*/
            // await GetAndSaveSchemaById("YHKH1xWTqwF24lvYNGguGA");
            // await GetTopic(stoppingToken);
        } catch (RpcException exc) {
            _logger.LogCritical(exc, "RPCException thrown with message: {message}", exc.Message);
            _logger.LogCritical("Status: {status}", exc.StatusCode);
            _logger.LogCritical("Shutting down application gracefully");
        } finally {
            _hostApplicationLifetime.StopApplication();
        }
    }

    private async Task ListenForChannelEventsStrategy(CancellationToken stoppingToken) {
        var fetchRequest = new FetchRequest {
            TopicName = "/data/MyCustomChannel__chn",
            NumRequested = 25
        };
        using var stream = _pubsubClient.Subscribe(null, null, stoppingToken);
        await stream.RequestStream.WriteAsync(fetchRequest, stoppingToken);

        var schemaDict = (await _metaRepo.GetCachedSchemas(stoppingToken))
            .ToDictionary(p => p.SchemaId, p => p);

        while (await stream.ResponseStream.MoveNext(stoppingToken)) {
            var latestReplayId = stream.ResponseStream.Current.LatestReplayId.ToLongBE();
            _logger.LogInformation("latest Replay Id: {replayId}", latestReplayId);
            _logger.LogInformation("Time: {rightNow} RPC ID: {RpcId}", DateTime.Now, stream.ResponseStream.Current.RpcId);

            var re = stream.ResponseStream.Current;
            Console.WriteLine("number of events in payload " + re.Events.Count);
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
                    var validSchemaId = schemaDict.TryGetValue(e.Event.SchemaId, out var dbSchema);
                    
                    // if an unhandeled schema id is encountered, log it and move on
                    if (!validSchemaId || dbSchema is null) {
                        _logger.LogWarning("Unrecognized Schema Id: {schemaId}", e.Event.SchemaId);
                        var schemaInfo = await GetAndSaveSchemaById(e.Event.SchemaId).ConfigureAwait(false);
                        // var avroSchema = Schema.Parse(schemaInfo.SchemaJson);
                        // var dbSchemaToInsert = new CDCSchema { 
                        //     EntityName = avroSchema.Name.Replace("ChangeEvent", ""), 
                        //     DbSchemaFullName = "salesforce.contacts", 
                        //     SchemaId = e.Event.SchemaId, 
                        //     SchemaName = avroSchema.Name
                        // };
                        // dbSchema = await _metaRepo.CreateNewSchema(dbSchemaToInsert).ConfigureAwait(false);
                        // schemaDict.Add(e.Event.SchemaId, dbSchema);
                    }
                    _logger.LogInformation("Processing: {schemaName} event", dbSchema.SchemaName);
                    
                    var schema = Schema.Parse(await File.ReadAllTextAsync($"./avro/{dbSchema.SchemaName}.avsc", stoppingToken));
                    
                    using var memStream = new MemoryStream(payload.ToByteArray());
                    var decoder = new BinaryDecoder(memStream);
                    var datumReader = new GenericDatumReader<GenericRecord>(schema, schema);
                    var gr = datumReader.Read(null, decoder);
                    var changeEventHeaderValid = gr.GetTypedValue<GenericRecord>("ChangeEventHeader", out var genericChangeEventHeader);
                    var changeTypeFound = genericChangeEventHeader.GetTypedValue<dynamic>("changeType", out var changeType);
                    var entityName = genericChangeEventHeader.GetValue(0).ToString();
                    Console.WriteLine(entityName + " HAS BEEN " + changeType.Value);

                    Enum.TryParse<ChangeType>(changeType.Value, out ChangeType changeTypeEnum);

                    var processEvent = _eventResolver.Resolve(changeTypeEnum);
                    
                    eventTasks.Add(processEvent.ProcessEvent(gr, schema, dbSchema, stoppingToken));
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
                Id = "123123", SchemaId = "i8wgJwwM-AVcDbFkbRl5Nw", Payload = null
            }
        };
        var request = new PublishRequest {
            TopicName = "/data/AccountChangeEvent",
            Events = { events }
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
        var name = $"{avroSchema.Name}.avsc";
        Console.WriteLine("saving schame as " + name);
        await File.WriteAllTextAsync($"./avro/{name}", someSchema.SchemaJson);
    }

    public async Task<SchemaInfo> GetAndSaveSchemaById(string schemaId) {
        var schemaRequest = new SchemaRequest {
            SchemaId = schemaId
        };
        var schemaInfo = await _pubsubClient.GetSchemaAsync(schemaRequest);
        var avroSchema = Schema.Parse(schemaInfo.SchemaJson);
        var name = $"{avroSchema.Name}.avsc";
        
        await File.WriteAllTextAsync($"./avro/{name}", schemaInfo.SchemaJson);

        return schemaInfo;
    }

    public async Task GetTopic(CancellationToken cancellationToken) {
        var topicRequest = new TopicRequest { TopicName = "/data/MyCustomChannel__chn" };
        var res = await _pubsubClient.GetTopicAsync(topicRequest, cancellationToken: cancellationToken);
        Console.WriteLine("Topic response " + res.ToJson());
    }


    public async Task TestMethod(CancellationToken cancellationToken = default) {
        // var eventsJson = File.ReadAllText("./AccountUpdateEvents.json");
        // var events = JsonSerializer.Deserialize<List<EventWrapper>>(eventsJson) ?? throw new NullReferenceException("Eventrs are null");
        // Console.WriteLine("Number of events " + events.Count);
        // var eventTasks = new List<Task>();
        // var accSchema = Schema.Parse(File.ReadAllText("./avro/AccountChangeEventGRPCSchema.avsc"));
        // foreach (var eve in events) {
        //     Console.WriteLine("event " + eve.ReplayId);
        //     // Console.WriteLine(eve.Event.Payload);
        //     var byteString = ByteString.CopyFromUtf8(eve.Event.Payload);
        //     var replayId = (ByteString.CopyFromUtf8(eve.ReplayId)).ToLongBE();
        //     Console.WriteLine(replayId);
        //     Console.WriteLine(byteString.Length);
        //     eventTasks.Add(_mediator.Send(new GenericCDCEventCommand {
        //         Name = "Account Change Event",
        //         AvroPayload = byteString.ToByteArray(),
        //         AvroSchema = accSchema
        //     }));
        // }
        //
        // await Task.WhenAll(eventTasks);


        await _client.GetRecordTypes(cancellationToken);
    }
}