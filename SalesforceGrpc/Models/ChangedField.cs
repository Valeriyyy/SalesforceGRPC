namespace SalesforceGrpc.Models;

/// <summary>
/// Represents a changed field with its name and value for SQL transformation
/// </summary>
public record ChangedField {
    public string FieldName { get; set; }
    public object? Value { get; set; }
    public string AvroTypeName { get; set; }

    public ChangedField(string fieldName, object? value, string avroTypeName) {
        FieldName = fieldName;
        Value = value;
        AvroTypeName = avroTypeName;
    }
}