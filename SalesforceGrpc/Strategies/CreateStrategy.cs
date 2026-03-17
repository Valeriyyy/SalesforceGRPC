using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database;
using Database.Models;
using Database.Repositories;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Models;

namespace SalesforceGrpc.Strategies;

public class CreateStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.CREATE;

    private readonly ILogger<CreateStrategy> _logger;

    private readonly IMetaRepository _db;

    public CreateStrategy(ILogger<CreateStrategy> logger, IMetaRepository db) {
        _logger = logger;
        _db = db;
    }
    
    public async Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        var json = record.ToString();
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
        
        var pgFieldMappings = await _db.GetCachedMapping(dbSchema.Id, cancellationToken).ConfigureAwait(false);
        

        record.Schema.Fields.ForEach(field => {
            if(record.TryGetValue(field.Name, out var value)) {
                _logger.LogInformation($"{field.Name}: {value}");
            }
            
            if(pgFieldMappings.TryGetValue(field.Name, out var pgFieldName)) {
                _logger.LogInformation($"Mapped {field.Name} to {pgFieldName}");
            } else {
                _logger.LogInformation($"No mapping found for {field.Name}");
            }
        });
        
        var changeSet = new RecordChangeSet("Account","RecordIds", ChangeType.CREATE);
        changeSet.ChangedFields.Add(new ChangedField("sf_id", recordIds[0].ToString() ?? string.Empty, "string"));
        var fieldTypeMapping = recSchema.GetFieldTypeMapping();
        record.Schema.Fields.ForEach(field => {
            if(record.TryGetValue(field.Name, out var value)) {
                Console.WriteLine($"{field.Name}: {value}");
            }
            
            if(pgFieldMappings.TryGetValue(field.Name, out var pgFieldName) && value != null) {
                Console.WriteLine($"Mapped {field.Name} to {pgFieldName} with value: {value}");

                var avroType = fieldTypeMapping.GetValueOrDefault(field.Name, "string");
                var fieldDoc = GetFieldDocumentation(recSchema, field.Name);
                var convertedValue = FieldTypeConverter.ConvertValue(value, avroType, fieldDoc);

                changeSet.ChangedFields.Add(new ChangedField(pgFieldName, convertedValue, avroType));
            } else {
                Console.WriteLine($"No mapping found for {field.Name}");
            }
        });

        var data = changeSet.ToDataObject();
        try {
            await _db.Create(dbSchema.DbSchemaFullName, data, cancellationToken).ConfigureAwait(false);
        } catch (Exception e) {
            _logger.LogCritical(e, "Failed to insert record {Data}", data.ToJson());
        }
    }

    public static async Task StaticProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        var json = record.ToString();
        // Extract change event header efficiently
        if (!record.TryGetValue("ChangeEventHeader", out var changeEventHeaderObj) || 
            changeEventHeaderObj is not GenericRecord changeEventHeader) {
            Console.WriteLine("No ChangeEventHeader found in record");
            return;
        }
        
        // Get record IDs from change event header
        if (!changeEventHeader.TryGetValue("recordIds", out var recordIdsObj) || 
            recordIdsObj is not object[] recordIds || recordIds.Length == 0) {
            Console.WriteLine("No record IDs found in ChangeEventHeader");
            return;
        }
        
        if (schema is not RecordSchema recSchema) {
            Console.WriteLine("Failed to load Account schema");
            return;
        }
        
        // var recordIdStrings = recordIds.Select(id => id.ToString() ?? string.Empty).ToList();
        // Console.WriteLine("These are the created records: {records}", string.Join(",", recordIdStrings));
        
        // var pgFieldMappings = await _db.GetCachedMapping(dbSchema.Id, cancellationToken).ConfigureAwait(false);
        // GenericRecord sfRecord = new GenericRecord(recSchema);
        // sfRecord.Add("Id", recordIds[0].ToString());
        
        var pgFieldMappings = (new List<MappedField> {
            new() { Id = 1, SalesforceFieldName = "Name", PostgresFieldName = "name" },
            new() { Id = 2, SalesforceFieldName = "Id", PostgresFieldName = "sf_id" },
            new() { Id = 3, SalesforceFieldName = "RecordTypeId", PostgresFieldName = "record_type_sf_id" },
            new() { Id = 4, SalesforceFieldName = "Phone", PostgresFieldName = "phone" }
        }).ToDictionary(m => m.SalesforceFieldName, m => m.PostgresFieldName);
        
        var changeSet = new RecordChangeSet("Account","RecordIds", ChangeType.CREATE);
        var fieldTypeMapping = recSchema.GetFieldTypeMapping();
        record.Schema.Fields.ForEach(field => {
            if(record.TryGetValue(field.Name, out var value)) {
                Console.WriteLine($"{field.Name}: {value}");
            }
            
            if(pgFieldMappings.TryGetValue(field.Name, out var pgFieldName)) {
                Console.WriteLine($"Mapped {field.Name} to {pgFieldName} with value: {value}");
                changeSet.ChangedFields.Add(new ChangedField(pgFieldName, value, "string"));
            } else {
                Console.WriteLine($"No mapping found for {field.Name}");
            }
        });

        var data = changeSet.ToDataObject();
    }
    
    private string? GetFieldDocumentation(RecordSchema schema, string fieldName) {
        var field = schema.Fields.FirstOrDefault(f => f.Name == fieldName);
        return field != null ? field.Documentation : null;
    }

    private string? GetNestedFieldDocumentation(RecordSchema? schema, string fieldName) {
        if (schema == null) return null;
        var field = schema.Fields.FirstOrDefault(f => f.Name == fieldName);
        return field != null ? field.Documentation : null;
    }


    public Task ProcessEvent(byte[] payload, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        _logger.LogInformation("This is where the insert process event goes");
        return Task.CompletedTask;
    }
}