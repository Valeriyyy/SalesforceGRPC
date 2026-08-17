using Avro;

namespace Salesforce.Avro;

/// <summary>
/// Reads the bindable field list out of an Entity's Avro schema.
/// </summary>
/// <remarks>
/// Compound Salesforce fields arrive as a nested Avro record — Name as Switchable_PersonName, BillingAddress
/// as Address, a geolocation as Location — and are flattened here by concatenating parent and child. That
/// concatenated form is what the worker's strategies look up in mapped_fields, so this class owns the naming
/// rule for both sides.
/// </remarks>
public static class EntityFieldReader {
    private const string ChangeEventHeaderField = "ChangeEventHeader";

    /// <summary>
    /// Builds the flattened name for one child of a compound field. The worker's UpdateStrategy composes the
    /// same string when it decodes a nested changed-fields bitmap.
    /// </summary>
    public static string FlattenedName(string parentName, string childName) => $"{parentName}{childName}";

    /// <summary>
    /// Returns every field of the Entity that a Field Mapping may name, with compound fields flattened.
    /// </summary>
    /// <remarks>
    /// A compound field is never returned under its own name — it has no single value to write, so a mapping
    /// naming it could not match. A field whose union carries both a scalar and a record (Account.Name, which
    /// is a string on a business account and a record on a person account) is returned in both forms.
    /// </remarks>
    public static IReadOnlyList<EntityField> ReadFields(RecordSchema schema) {
        ArgumentNullException.ThrowIfNull(schema);

        var fields = new List<EntityField>();

        foreach (var field in schema.Fields) {
            if (field.Name == ChangeEventHeaderField) {
                continue;
            }

            var doc = SalesforceFieldDoc.Parse(field.Documentation);
            var members = UnionMembers(field.Schema, out var isNullable);

            foreach (var member in members) {
                if (member is RecordSchema nested) {
                    fields.AddRange(ReadCompoundChildren(field.Name, nested, doc));
                } else {
                    fields.Add(new EntityField {
                        Name = field.Name,
                        // A scalar alongside a record in the same union (Account.Name) is documented with the
                        // record's type, which does not describe the scalar. Fall back to the Avro type.
                        FieldType = doc.FieldType.IsCompound() ? FromAvroType(member) : doc.FieldType,
                        AvroType = AvroTypeName(member),
                        IsNullable = isNullable,
                        FieldId = doc.FieldId
                    });
                }
            }
        }

        return fields;
    }

    private static IEnumerable<EntityField> ReadCompoundChildren(string parentName, RecordSchema nested,
        SalesforceFieldDoc parentDoc) {
        foreach (var child in nested.Fields) {
            var childMembers = UnionMembers(child.Schema, out var childNullable);
            // A compound child is always scalar — Salesforce nests records only one level deep.
            var childSchema = childMembers.FirstOrDefault(m => m is not RecordSchema);
            if (childSchema is null) {
                continue;
            }

            var childDoc = SalesforceFieldDoc.Parse(child.Documentation);

            yield return new EntityField {
                Name = FlattenedName(parentName, child.Name),
                // Salesforce writes no doc on compound children, so the Avro type is the only source. That is
                // safe here because no compound carries a temporal or currency child, which are the types the
                // Avro layer cannot tell apart.
                FieldType = childDoc.FieldType is SalesforceFieldType.Unknown
                    ? FromAvroType(childSchema)
                    : childDoc.FieldType,
                AvroType = AvroTypeName(childSchema),
                IsNullable = childNullable,
                ParentName = parentName,
                ChildName = child.Name,
                FieldId = parentDoc.FieldId
            };
        }
    }

    /// <summary>
    /// Unwraps a union into its non-null members, reporting whether null was among them.
    /// </summary>
    private static List<Schema> UnionMembers(Schema schema, out bool isNullable) {
        if (schema is not UnionSchema union) {
            isNullable = false;
            return [schema];
        }

        isNullable = union.Schemas.Any(s => s.Tag == Schema.Type.Null);
        return union.Schemas.Where(s => s.Tag != Schema.Type.Null).ToList();
    }

    private static string AvroTypeName(Schema schema) => schema.Tag switch {
        Schema.Type.String => "string",
        Schema.Type.Long => "long",
        Schema.Type.Int => "int",
        Schema.Type.Double => "double",
        Schema.Type.Float => "float",
        Schema.Type.Boolean => "boolean",
        Schema.Type.Bytes => "bytes",
        _ => schema.Name
    };

    private static SalesforceFieldType FromAvroType(Schema schema) => schema.Tag switch {
        Schema.Type.String => SalesforceFieldType.Text,
        Schema.Type.Boolean => SalesforceFieldType.Boolean,
        Schema.Type.Int or Schema.Type.Long => SalesforceFieldType.Integer,
        Schema.Type.Double or Schema.Type.Float => SalesforceFieldType.Double,
        _ => SalesforceFieldType.Unknown
    };
}
