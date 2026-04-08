using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database.Models;
using Database.Repositories;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Models;
using static System.Console;

namespace SalesforceGrpc.Strategies;

public class CreateStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.CREATE;

    private readonly ILogger<CreateStrategy> _logger;
    private readonly IMetaRepository _db;

    public CreateStrategy(ILogger<CreateStrategy> logger, IMetaRepository db) {
        _logger = logger;
        _db = db;
    }

    public async Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema,
        CancellationToken cancellationToken) {
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
            _logger.LogError("Failed to load schema");
            return;
        }

        var recordIdStrings = recordIds.Select(id => id.ToString() ?? string.Empty).ToList();
        _logger.LogInformation("Processing created records: {records}", string.Join(",", recordIdStrings));

        // Get cached field mappings (Salesforce -> PostgreSQL)
        var pgFieldMappings = await _db.GetCachedMapping(dbSchema.Id, cancellationToken).ConfigureAwait(false);

        // For CREATE events, process ALL mapped fields (not just changed ones)
        var allChangedFields = ProcessAllFieldValues(record, recSchema, pgFieldMappings);

        // Create RecordChangeSet with all record IDs
        var changeSet = new RecordChangeSet(dbSchema.EntityName, recordIdStrings, ChangeType.CREATE);
        changeSet.ChangedFields.Add(new ChangedField("sf_id", recordIds[0].ToString() ?? string.Empty, "string"));

        foreach (var field in allChangedFields) {
            changeSet.ChangedFields.Add(field);
        }

        // final data object that will be inserted into database
        var data = changeSet.ToDataObject();

        try {
            await _db.Create(dbSchema.DbSchemaFullName, data, cancellationToken).ConfigureAwait(false);
        } catch (Exception e) {
            _logger.LogCritical(e, "Failed to insert record {Data}", data.ToJson());
        }
    }

    private List<ChangedField> ProcessAllFieldValues(GenericRecord sfRecord, RecordSchema recSchema,
        Dictionary<string, string> pgFieldMappings) {
        var changedFields = new List<ChangedField>();

        // Get field type mapping for all fields
        var fieldTypeMapping = recSchema.GetFieldTypeMapping();

        WriteLine($"\n=== Processing All Fields for CREATE Event ===");
        foreach (var field in recSchema.Fields) {
            if (!sfRecord.TryGetValue(field.Name, out var fieldValue)) {
                continue;
            }

            WriteLine($"  Field: {field.Name}, Value: {fieldValue}");

            // Check if this is a nested field (UnionSchema)
            var fieldSchema = field.Schema;

            if (fieldSchema is UnionSchema) {
                var nestedRecordSchema = GetNestedRecordSchema(recSchema, field.Pos);

                if (nestedRecordSchema != null && fieldValue is GenericRecord nestedRecord) {
                    WriteLine($"  Detected nested field: {field.Name}");

                    // Process nested fields
                    var nestedChangedFields =
                        ProcessNestedFieldValues(nestedRecord, recSchema, field.Pos, pgFieldMappings);
                    changedFields.AddRange(nestedChangedFields);

                    // Skip adding the nested field itself as a top-level field
                    continue;
                }
            }

            // Try to map top-level field to PostgreSQL field name
            if (pgFieldMappings.TryGetValue(field.Name, out var pgFieldName) && fieldValue != null) {
                var avroType = fieldTypeMapping.GetValueOrDefault(field.Name, "string");
                var fieldDoc = GetFieldDocumentation(recSchema, field.Name);
                var convertedValue = FieldTypeConverter.ConvertValue(fieldValue, avroType, fieldDoc);

                changedFields.Add(new ChangedField(pgFieldName, convertedValue, avroType));
                WriteLine($"    Mapped to: {pgFieldName} = {convertedValue} ({avroType})");
            } else if (fieldValue != null) {
                WriteLine($"    No mapping found for field: {field.Name}");
            }
        }

        return changedFields;
    }

    private List<ChangedField> ProcessNestedFieldValues(GenericRecord nestedRecord, RecordSchema recSchema,
        int avroFieldNumber, Dictionary<string, string> pgFieldMappings) {
        var changedFields = new List<ChangedField>();

        if (avroFieldNumber < 0 || avroFieldNumber >= recSchema.Fields.Count) {
            return changedFields;
        }

        var nestedFieldName = recSchema.Fields[avroFieldNumber].Name;

        // Get the field type mapping for nested fields
        var nestedFieldTypeMapping = recSchema.GetNestedFieldTypeMapping(avroFieldNumber);
        var nestedRecordSchema = GetNestedRecordSchema(recSchema, avroFieldNumber);

        WriteLine($"    Nested field '{nestedFieldName}' values:");

        foreach (var nestedField in nestedRecordSchema?.Fields ?? new List<Field>()) {
            if (!nestedRecord.TryGetValue(nestedField.Name, out var fieldValue)) {
                continue;
            }

            // Create the nested field key (e.g., PersonNameFirstName for FirstName in PersonName)
            var sfNestedFieldKey = $"{nestedFieldName}{nestedField.Name}";

            WriteLine($"      {nestedField.Name}: {fieldValue}");

            // Try to map to PostgreSQL field name
            if (pgFieldMappings.TryGetValue(sfNestedFieldKey, out var pgFieldName) && fieldValue != null) {
                var avroType = nestedFieldTypeMapping.GetValueOrDefault(nestedField.Name, "string");
                var fieldDoc = GetNestedFieldDocumentation(nestedRecordSchema, nestedField.Name);
                var convertedValue = FieldTypeConverter.ConvertValue(fieldValue, avroType, fieldDoc);

                changedFields.Add(new ChangedField(pgFieldName, convertedValue, avroType));
                WriteLine($"        Mapped to: {pgFieldName} = {convertedValue} ({avroType})");
            } else if (fieldValue != null) {
                WriteLine($"        No mapping found for nested field: {sfNestedFieldKey}");
            }
        }

        return changedFields;
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

    private RecordSchema? GetNestedRecordSchema(RecordSchema schema, int fieldIndex) {
        if (fieldIndex < 0 || fieldIndex >= schema.Fields.Count) return null;

        var field = schema.Fields[fieldIndex];
        var fieldSchema = field.Schema;

        // Handle union types (fields may be [null, SomeType])
        if (fieldSchema is UnionSchema unionSchema) {
            return unionSchema.Schemas.OfType<RecordSchema>().FirstOrDefault();
        }

        return fieldSchema as RecordSchema;
    }
}