using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database;

namespace SalesforceGrpc.Strategies;

public class InsertStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.CREATE;
    private readonly ILogger<InsertStrategy> _logger;

    public InsertStrategy(ILogger<InsertStrategy> logger) {
        _logger = logger;
    }
    public async Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        // Extract change event header efficiently
        if (!record.TryGetValue("ChangeEventHeader", out var changeEventHeaderObj) || 
            changeEventHeaderObj is not GenericRecord changeEventHeader) {
            _logger.LogWarning("No ChangeEventHeader found in record");
            return;
        }
        
        // Get record IDs from change event header
        if (!changeEventHeader.TryGetValue("recordIds", out var recordIdsObj) || 
            recordIdsObj is not object[] recordIds || recordIds.Length == 0) {
            _logger.LogWarning("No record IDs found in ChangeEventHeader");
            return;
        }
        
        if (schema is not RecordSchema recSchema) {
            _logger.LogError("Failed to load Account schema");
            return;
        }
        
        var recordIdStrings = recordIds.Select(id => id.ToString() ?? string.Empty).ToList();
        _logger.LogInformation("These are the created records: {records}", string.Join(",", recordIdStrings));
    }


    public Task ProcessEvent(byte[] payload, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        _logger.LogInformation("This is where the insert process event goes");
        return Task.CompletedTask;
    }
}