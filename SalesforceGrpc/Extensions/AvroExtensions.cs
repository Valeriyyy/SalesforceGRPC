using Avro.Generic;
using Avro.Specific;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SalesforceGrpc.Extensions;
public static class AvroExtensions {
    /// <summary>
    /// AOT-compatible extension for ISpecificRecord.
    /// Uses explicit property reflection with trim safety via BindingFlags.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetFieldNamesDict is only called on generated Avro types which are preserved in TrimmerRootDescriptor")]
    public static Dictionary<int, string> GetFieldNamesDict(this ISpecificRecord record) {
        var properties = record
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            .Where(f => !f.Name.Equals("Schema", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = new Dictionary<int, string>();
        for (int i = 0; i < properties.Count; i++) {
            result[i] = properties[i].Name;
        }
        return result;
    }

    /// <summary>
    /// AOT-safe generic value extraction from GenericRecord.
    /// No reflection needed for this method.
    /// </summary>
    public static bool GetTypedValue<T>(this GenericRecord gr, string fieldName, out T value) {
        if (gr.TryGetValue(fieldName, out object? fieldValue) && fieldValue is T t) {
            value = t;
            return true;
        }
        value = default;
        return false;
    }
}
