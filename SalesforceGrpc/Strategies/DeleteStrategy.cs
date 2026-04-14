using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database;
using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;

namespace SalesforceGrpc.Strategies;

public class DeleteStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.DELETE;
    
    private readonly ILogger<DeleteStrategy> _logger;
    private readonly IDataRepository _dataRepo;

    public DeleteStrategy(ILogger<DeleteStrategy> logger, IDataRepository dataRepo) {
        _logger = logger;
        _dataRepo = dataRepo;
    }

    public async Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
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
        
        var recordIdStrings = recordIds.Select(id => id.ToString() ?? string.Empty).ToList();
        _logger.LogInformation("Processing created records: {records}", string.Join(",", recordIdStrings));

        try {
            // For DELETE events, we only need to delete by record ID (no field values needed)
            var deletedCount = await _dataRepo.Delete(dbSchema.DbSchemaFullName, recordIdStrings).ConfigureAwait(false);
            _logger.LogInformation("Deleted {DeletedCount} records from {ObjectType}", deletedCount, dbSchema.EntityName);
        } catch (Exception e) {
            _logger.LogCritical(e, "Failed to delete the following {ObjectType} records: {recordIds}", dbSchema.EntityName, string.Join(",", recordIdStrings));
        }
    }
}