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
    private readonly IAvroSchemaRepository _avroSchemaRepo;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public Worker(
        ILogger<Worker> logger,
        IOptions<SalesforceConfig> sfconfig,
        IConfiguration config,
        PubSub.PubSubClient psClient,
        IHostApplicationLifetime hostApplicationLifetime, 
        SalesforceClient client, 
        IMetaRepository metaRepo,
        IAvroSchemaRepository avroSchemaRepo,
        EventResolver eventResolver) {
        _logger = logger;
        _sfconfig = sfconfig.Value;
        _pubsubClient = psClient;
        _config = config;
        _hostApplicationLifetime = hostApplicationLifetime;
        _client = client;
        _metaRepo = metaRepo;
        _avroSchemaRepo = avroSchemaRepo;
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
            .ToDictionary(p => p.AvroSchema.SchemaId, p => p);

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
                    
                    // the event has a schema id, we need to check if we have it in our dictionary, if not fetch and save it
                    _logger.LogInformation("Schema Id: {schemaId}", e.Event.SchemaId);
                    var validSchemaId = schemaDict.TryGetValue(e.Event.SchemaId, out var dbSchema);
                    
                    // if an unhandled schema id is encountered, fetch and save it
                    if (!validSchemaId || dbSchema is null) {
                        _logger.LogWarning("Unrecognized Schema Id: {schemaId}", e.Event.SchemaId);
                        dbSchema = await GetAndSaveSchemaById(e.Event.SchemaId).ConfigureAwait(false);
                        if (dbSchema is not null) {
                            // add the new schema to the dictionary for future reference
                            schemaDict.Add(e.Event.SchemaId, dbSchema);
                            // find and remove the old schema, probably bad/slow if there are lots of schemas in dictionary
                            var oldSchema = schemaDict.Values.FirstOrDefault(s => s.SchemaName == dbSchema.SchemaName && s.SchemaId != dbSchema.SchemaId);
                            if (oldSchema is not null) {
                                schemaDict.Remove(oldSchema.AvroSchema.SchemaId);
                            }
                        } else {
                            _logger.LogError("Failed to retrieve schema with ID: {SchemaId}", e.Event.SchemaId);
                            continue;
                        }
                    }
                    _logger.LogInformation("Processing: {schemaName} event", dbSchema.SchemaName);
                    
                    // Use Avro schema from database if available, otherwise fall back to file
                    var schemaJson = dbSchema.AvroSchema?.SchemaJson ?? await File.ReadAllTextAsync($"./avro/{dbSchema.SchemaName}.avsc", stoppingToken).ConfigureAwait(false);
                    var schema = Schema.Parse(schemaJson);
                    
                    using var memStream = new MemoryStream(payload.ToByteArray());
                    var decoder = new BinaryDecoder(memStream);
                    var datumReader = new GenericDatumReader<GenericRecord>(schema, schema);
                    var gr = datumReader.Read(null, decoder);
                    gr.GetTypedValue<GenericRecord>("ChangeEventHeader", out var genericChangeEventHeader);
                    genericChangeEventHeader.GetTypedValue<dynamic>("changeType", out var changeType);
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

    private async Task<CDCSchema?> GetAndSaveSchemaById(string schemaId) {
        try {
            var schemaRequest = new SchemaRequest {
                SchemaId = schemaId
            };
            var schemaInfo = await _pubsubClient.GetSchemaAsync(schemaRequest).ConfigureAwait(false);
            var avroSchema = Schema.Parse(schemaInfo.SchemaJson);
            var fileName = $"{avroSchema.Name}.avsc";
            
            // Save Avro schema to database
            var dbAvroSchema = new DbAvroSchema {
                SchemaId = schemaInfo.SchemaId,
                RecordName = avroSchema.Name,
                SchemaJson = schemaInfo.SchemaJson,
                DateCreated = DateTime.UtcNow,
                DateUpdated = null
            };
            
            var avroSchemaId = await _avroSchemaRepo.InsertSchemaAsync(dbAvroSchema).ConfigureAwait(false);
            _logger.LogInformation("Saved Avro schema to database with ID: {AvroSchemaId}, SchemaId: {SchemaId}", avroSchemaId, schemaInfo.SchemaId);
            
            // Check if CDC schema already exists for this schema_id
            var existingCdcSchema = await _metaRepo.GetSchemaByRecordName(avroSchema.Name).ConfigureAwait(false);
            
            CDCSchema? savedCdcSchema;
            
            if (existingCdcSchema != null) {
                // Update existing CDC schema with new Avro schema link
                var updateSuccess = await _metaRepo.UpdateCdcSchemaWithAvroLink(existingCdcSchema.Id, avroSchemaId).ConfigureAwait(false);
                
                if (updateSuccess) {
                    _logger.LogInformation("Updated existing CDC schema {CdcSchemaId} with Avro schema link {AvroSchemaId}", 
                        existingCdcSchema.Id, avroSchemaId);
                    
                    // Refresh the schema from database to get updated Avro link
                    savedCdcSchema = await _metaRepo.GetSchemaById(existingCdcSchema.Id).ConfigureAwait(false);
                } else {
                    _logger.LogWarning("Failed to update CDC schema {CdcSchemaId} with Avro schema link", existingCdcSchema.Id);
                    return existingCdcSchema;
                }
            } else {
                // Create new CDC schema record and link to Avro schema
                var entityName = avroSchema.Name.Replace("ChangeEvent", "");
                var cdcSchema = new CDCSchema {
                    EntityName = entityName,
                    SchemaId = schemaInfo.SchemaId,
                    SchemaName = avroSchema.Name,
                    DbSchemaFullName = $"salesforce.{entityName.ToLower()}",
                    AvroSchema = dbAvroSchema
                };
                
                savedCdcSchema = await _metaRepo.CreateNewSchemaWithAvroLink(cdcSchema, avroSchemaId).ConfigureAwait(false);
                _logger.LogInformation("Created new CDC schema: {SchemaName} with Avro schema link", savedCdcSchema.SchemaName);
            }
            
            // Also save to file for backwards compatibility (optional)
            await File.WriteAllTextAsync($"./avro/{fileName}", schemaInfo.SchemaJson).ConfigureAwait(false);
            
            return savedCdcSchema;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving and saving schema with ID: {SchemaId}", schemaId);
            return null;
        }
    }
}