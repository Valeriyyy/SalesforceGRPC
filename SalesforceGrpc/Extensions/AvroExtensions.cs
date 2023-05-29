using Avro.Specific;

namespace SalesforceGrpc.Extensions;
public static class AvroExtensions {
    public static Dictionary<int, string> GetFieldNamesDict(this ISpecificRecord record) => record
        .GetType()
        .GetProperties()
        .Where(f => f.Name != "Schema")
        .Select((obj, index) => new { Key = index, Value = obj.Name })
        .ToDictionary(p => p.Key, p => p.Value);
}
