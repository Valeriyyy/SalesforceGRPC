using Avro;
using Avro.Generic;
using com.sforce.eventbus;
using Database;
using Database.Models;
using Database.Repositories;
using SalesforceGrpc.Extensions;
using SalesforceGrpc.Models;
using System.Collections;
using static System.Console;

namespace SalesforceGrpc.Strategies;

public class UpdateStrategy : IEventStrategy {
    public ChangeType ChangeType => ChangeType.UPDATE;
    private readonly ILogger<UpdateStrategy> _logger;
    private readonly IMetaRepository _db;
    
    public UpdateStrategy(ILogger<UpdateStrategy> logger, IMetaRepository db) {
        _logger = logger;
        _db = db;
    }
    
    public async Task ProcessEvent(GenericRecord record, Schema schema, CDCSchema dbSchema, CancellationToken cancellationToken) {
        var schemas = (await _db.GetCDCSchemas(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.EntityName, s => s.DbSchemaFullName);
        
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
        
        // Get cached field mappings (Salesforce -> PostgreSQL)
        // var pgFieldMappings = FieldMappingCache.GetOrCreateMapping(request.EntityName, fieldMappings);
        var pgFieldMappings = await _db.GetCachedMapping(dbSchema.Id, cancellationToken).ConfigureAwait(false);

        // Process changed fields if present
        var changedFieldsList = new List<ChangedField>();
        if (changeEventHeader.TryGetValue("changedFields", out var changedFieldsObj) && 
            changedFieldsObj is object[] changedFields && changedFields.Length > 0) {
            changedFieldsList = ProcessChangedFields(record, changedFields, recSchema, pgFieldMappings);
        }
            
        // Create RecordChangeSet with all record IDs
        var recordIdStrings = recordIds.Select(id => id.ToString() ?? string.Empty).ToList();
        var changeSet = new RecordChangeSet(schemas[dbSchema.EntityName], recordIdStrings, ChangeType.UPDATE);
        foreach (var field in changedFieldsList) {
            changeSet.ChangedFields.Add(field);
        }
            
        // Generate and log SQL statement (single statement for all records)
        var sqlStatement = changeSet.ToSqlUpdateStatement();
        var data = changeSet.ToDataObject();
        _logger.LogInformation(sqlStatement);
        _logger.LogDebug(sqlStatement);
        if(data.Count == 0) return;

        try {
            await _db.Update(schemas[dbSchema.EntityName], recordIdStrings, data, cancellationToken);
        } catch (Exception e) {
            _logger.LogCritical(e, "Failed to update record {Data}", data.ToJson());
        }
    }
    
    private List<ChangedField> ProcessChangedFields(GenericRecord sfRecord, object[] changedFields, 
        RecordSchema recSchema, Dictionary<string, string> pgFieldMappings) {
        var changedFieldsList = new List<ChangedField>();
        WriteLine($"\n=== Decoding Changed Fields Bitmap ===");
        WriteLine($"Number of changed field bitmaps: {changedFields.Length}");

        foreach (var changedFieldHex in changedFields) {
            var changedFieldStr = changedFieldHex.ToString() ?? "";
            if (string.IsNullOrEmpty(changedFieldStr)) continue;

            WriteLine($"\nBitmap entry: {changedFieldStr}");

            // Check if this is a nested field change (contains a dash)
            var dashIndex = changedFieldStr.IndexOf('-');
            if (dashIndex > 0) {
                // Nested field changed
                var avroFieldNumber = int.Parse(changedFieldStr.AsSpan(0, dashIndex));
                var hexBitMap = changedFieldStr.AsSpan(dashIndex + 1);
                WriteLine($"  Nested field change at AVRO field position: {avroFieldNumber}");
                WriteLine($"  Hex bitmap: {hexBitMap}");

                var decodedNestedFields = DecodeChangedFieldsBitmap(hexBitMap.ToString(), recSchema, avroFieldNumber);
                WriteLine($"  Changed nested fields: {string.Join(", ", decodedNestedFields)}");

                var nestedChangedFields = ProcessNestedFieldValues(sfRecord, recSchema, avroFieldNumber, 
                    decodedNestedFields, pgFieldMappings);
                changedFieldsList.AddRange(nestedChangedFields);
            } else {
                // Top-level field changed
                WriteLine("  Top-level field change");
                WriteLine($"  Hex bitmap: {changedFieldStr}");

                var decodedFields = DecodeChangedFieldsBitmap(changedFieldStr, recSchema, -1);
                WriteLine($"  Changed fields: {string.Join(", ", decodedFields)}");
                
                var topLevelChangedFields = ProcessTopLevelFieldValues(sfRecord, decodedFields, 
                    recSchema, pgFieldMappings);
                changedFieldsList.AddRange(topLevelChangedFields);
            }
        }

        return changedFieldsList;
    }

    private List<ChangedField> ProcessNestedFieldValues(GenericRecord sfRecord, RecordSchema recSchema, 
        int avroFieldNumber, List<string> decodedNestedFields, Dictionary<string, string> pgFieldMappings) {
        var changedFields = new List<ChangedField>();
        
        if (avroFieldNumber < 0 || avroFieldNumber >= recSchema.Fields.Count) {
            return changedFields;
        }

        var nestedFieldName = recSchema.Fields[avroFieldNumber].Name;
        if (!sfRecord.TryGetValue(nestedFieldName, out var nestedFieldValue) || 
            nestedFieldValue is not GenericRecord nestedRecord) {
            _logger.LogWarning($"Could not find or cast nested field '{nestedFieldName}' as GenericRecord");
            return changedFields;
        }

        // Get the field type mapping for nested fields
        var nestedFieldTypeMapping = recSchema.GetNestedFieldTypeMapping(avroFieldNumber);
        var nestedRecordSchema = GetNestedRecordSchema(recSchema, avroFieldNumber);
        
        WriteLine($"  Nested field '{nestedFieldName}' values:");
        foreach (var decodedField in decodedNestedFields) {
            // Create the nested field key (e.g., PersonNameFirstName for FirstName in PersonName)
            var sfNestedFieldKey = $"{nestedFieldName}{decodedField}";
            
            if (!nestedRecord.TryGetValue(decodedField, out var fieldValue)) {
                _logger.LogWarning($"Field '{decodedField}' not found in nested record '{nestedFieldName}'");
                continue;
            }

            WriteLine($"    {decodedField}: {fieldValue}");

            // Try to map to PostgreSQL field name
            if (pgFieldMappings.TryGetValue(sfNestedFieldKey, out var pgFieldName)) {
                var avroType = nestedFieldTypeMapping.GetValueOrDefault(decodedField, "string");
                var fieldDoc = GetNestedFieldDocumentation(nestedRecordSchema, decodedField);
                var convertedValue = FieldTypeConverter.ConvertValue(fieldValue, avroType, fieldDoc);
                
                changedFields.Add(new ChangedField(pgFieldName, convertedValue, avroType));
                WriteLine($"      Mapped to: {pgFieldName} = {convertedValue} ({avroType})");
            } else {
                _logger.LogWarning($"No mapping found for nested field '{sfNestedFieldKey}'");
            }
        }

        return changedFields;
    }

    private List<ChangedField> ProcessTopLevelFieldValues(GenericRecord sfRecord, List<string> decodedFields, 
        RecordSchema recSchema, Dictionary<string, string> pgFieldMappings) {
        var changedFields = new List<ChangedField>();
        
        // Get field type mapping for top-level fields
        var fieldTypeMapping = recSchema.GetFieldTypeMapping();
        
        WriteLine($"  Field values:");
        foreach (var fieldName in decodedFields) {
            if (!sfRecord.TryGetValue(fieldName, out var fieldValue)) {
                _logger.LogWarning($"Field '{fieldName}' not found in record");
                continue;
            }

            WriteLine($"    {fieldName}: {fieldValue}");

            // Try to map to PostgreSQL field name
            if (pgFieldMappings.TryGetValue(fieldName, out var pgFieldName)) {
                var avroType = fieldTypeMapping.GetValueOrDefault(fieldName, "string");
                var fieldDoc = GetFieldDocumentation(recSchema, fieldName);
                var convertedValue = FieldTypeConverter.ConvertValue(fieldValue, avroType, fieldDoc);
                
                changedFields.Add(new ChangedField(pgFieldName, convertedValue, avroType));
                WriteLine($"      Mapped to: {pgFieldName} = {convertedValue} ({avroType})");
            } else {
                _logger.LogWarning("No mapping found for Salesforce field '{FieldName}'", fieldName);
            }
        }

        return changedFields;
    }

    private List<string> DecodeChangedFieldsBitmap(string hexBitmap, Schema schema, int nestedFieldIndex) {
        var fieldNames = new List<string>();
        
        try {
            // Extract the hex value (remove "0x" prefix if present) more efficiently
            var hexValue = ExtractHexValue(hexBitmap);
            
            // Convert hex string to bytes and reverse them
            var bytes = Convert.FromHexString(hexValue);
            Array.Reverse(bytes);
            var bits = new BitArray(bytes);
            
            // Get the appropriate schema based on whether this is nested or top-level
            var targetSchema = ResolveTargetSchema(schema, nestedFieldIndex);
            
            // Extract field names from the target schema
            if (targetSchema is RecordSchema recSchema) {
                var fieldsCount = recSchema.Fields.Count;
                var bitsLength = bits.Length;
                for (int i = 0; i < bitsLength && i < fieldsCount; i++) {
                    if (bits[i]) {
                        fieldNames.Add(recSchema.Fields[i].Name);
                    }
                }
            }
        } catch (Exception ex) {
            WriteLine($"Error decoding bitmap: {ex.Message}");
        }
        
        return fieldNames;
    }

    private static string ExtractHexValue(string hexBitmap) {
        var xIndex = hexBitmap.LastIndexOf('x');
        return xIndex >= 0 ? hexBitmap.Substring(xIndex + 1) : hexBitmap;
    }

    private static Schema ResolveTargetSchema(Schema schema, int nestedFieldIndex) {
        if (nestedFieldIndex < 0 || schema is not RecordSchema recordSchema) {
            return schema;
        }

        if (nestedFieldIndex >= recordSchema.Fields.Count) {
            return schema;
        }

        var field = recordSchema.Fields[nestedFieldIndex];
        
        // Handle union types (fields may be [null, SomeType])
        if (field.Schema is UnionSchema unionSchema) {
            foreach (var s in unionSchema.Schemas) {
                if (s is RecordSchema) {
                    return s;
                }
            }
        }
        
        return field.Schema;
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