using Avro;

namespace SalesforceGrpc.Extensions;

/// <summary>
/// Extensions for working with Avro schemas to extract field type information
/// </summary>
public static class AvroSchemaExtensions {
    /// <summary>
    /// Extracts a mapping of field names to their Avro type names from a RecordSchema
    /// </summary>
    public static Dictionary<string, string> GetFieldTypeMapping(this RecordSchema schema) {
        var typeMapping = new Dictionary<string, string>();
        
        foreach (var field in schema.Fields) {
            var avroTypeName = GetAvroTypeName(field.Schema);
            typeMapping[field.Name] = avroTypeName;
        }
        
        return typeMapping;
    }

    /// <summary>
    /// Gets the Avro type name from a schema, handling union types
    /// </summary>
    private static string GetAvroTypeName(Schema schema) {
        if (schema is UnionSchema unionSchema) {
            // For union types like [null, string], get the non-null type
            foreach (var s in unionSchema.Schemas) {
                if (!(s is PrimitiveSchema ps && ps.Name == "null")) {
                    return GetAvroTypeName(s);
                }
            }
        }
        
        // Use schema name to determine type
        var schemaName = schema.Name?.ToLowerInvariant() ?? schema.ToString().ToLowerInvariant();
        
        return schemaName switch {
            "string" => "string",
            "int" => "int",
            "long" => "long",
            "float" => "float",
            "double" => "double",
            "boolean" => "boolean",
            "bytes" => "bytes",
            _ when schema is RecordSchema => "record",
            _ when schema is ArraySchema => "array",
            _ => schemaName
        };
    }

    /// <summary>
    /// Resolves a nested schema to get field type mapping for nested records
    /// </summary>
    public static Dictionary<string, string> GetNestedFieldTypeMapping(this RecordSchema parentSchema, int fieldIndex) {
        if (fieldIndex < 0 || fieldIndex >= parentSchema.Fields.Count) {
            return new Dictionary<string, string>();
        }

        var field = parentSchema.Fields[fieldIndex];
        var nestedSchema = field.Schema;

        // Handle union types
        if (nestedSchema is UnionSchema unionSchema) {
            foreach (var s in unionSchema.Schemas) {
                if (s is RecordSchema recordSchema) {
                    nestedSchema = recordSchema;
                    break;
                }
            }
        }

        if (nestedSchema is RecordSchema recSchema) {
            return recSchema.GetFieldTypeMapping();
        }

        return new Dictionary<string, string>();
    }
}



