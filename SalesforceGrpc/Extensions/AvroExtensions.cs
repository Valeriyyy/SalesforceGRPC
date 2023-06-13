using Avro.Generic;
using Avro.Specific;

namespace SalesforceGrpc.Extensions;
public static class AvroExtensions {
    public static Dictionary<int, string> GetFieldNamesDict(this ISpecificRecord record) => record
        .GetType()
        .GetProperties()
        .Where(f => f.Name != "Schema")
        .Select((obj, index) => new { Key = index, Value = obj.Name })
        .ToDictionary(p => p.Key, p => p.Value);


    public static bool GetTypedValue<T>(this GenericRecord gr, string fieldName, out T value) {
        if (gr.TryGetValue(fieldName, out object? fieldValue) && fieldValue is T t) {
            value = t;
            return true;
        }
        value = default;
        return false;
    }
}
