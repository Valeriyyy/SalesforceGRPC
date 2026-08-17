using Salesforce.Avro;

namespace SalesforceGrpc.Extensions;

/// <summary>
/// Converts a decoded Avro value into what the target database column expects.
/// </summary>
/// <remarks>
/// The Avro type alone cannot decide this: Date, DateTime and Time all arrive as a long, so the Salesforce
/// Field Type from the schema's doc annotation is what distinguishes them. That annotation is read by
/// <see cref="SalesforceFieldDoc"/>, the same parser the Binding validation uses, so the type a user
/// validated a mapping against and the type a value is converted with cannot diverge.
/// </remarks>
public static class FieldTypeConverter {
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Converts a value using the field's doc annotation, falling back to the Avro type when there is none.
    /// </summary>
    public static object? ConvertValue(object? value, string avroType, string? fieldDoc = null) {
        if (value == null) {
            return null;
        }

        var fieldType = SalesforceFieldDoc.Parse(fieldDoc).FieldType;

        switch (fieldType) {
            case SalesforceFieldType.DateTime:
                return ConvertEpochToDateTime(Convert.ToInt64(value));
            case SalesforceFieldType.DateOnly:
                return ConvertEpochToDate(Convert.ToInt64(value));
            case SalesforceFieldType.TimeOnly:
                return ConvertEpochToTime(Convert.ToInt64(value));
        }

        // Fallback to type-based detection when the schema carries no usable doc annotation.
        var normalizedType = avroType.Split('|')[0].Trim().ToLowerInvariant();

        return normalizedType switch {
            "long" => Convert.ToInt64(value),
            "int" => Convert.ToInt32(value),
            "double" => Convert.ToDouble(value),
            "float" => Convert.ToSingle(value),
            "boolean" => Convert.ToBoolean(value),
            // Passed through unchanged. Every repository writes values as parameters, so escaping here would
            // double up: an apostrophe would reach the target table as two.
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Converts epoch milliseconds to a DateTime.
    /// </summary>
    private static DateTime? ConvertEpochToDateTime(long epochMilliseconds) {
        try {
            return UnixEpoch.AddMilliseconds(epochMilliseconds);
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Converts a Salesforce Date field to a DateTime at midnight.
    /// </summary>
    /// <remarks>
    /// Identical to <see cref="ConvertEpochToDateTime"/> on purpose, and not a copy-paste slip: Salesforce
    /// sends a Date as an epoch datetime in milliseconds whose time component is already zeroed, so reading it
    /// the same way lands on the right date at midnight.
    /// </remarks>
    private static DateTime? ConvertEpochToDate(long epochMilliseconds) {
        try {
            return UnixEpoch.AddMilliseconds(epochMilliseconds);
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Converts milliseconds since midnight to a TimeOnly.
    /// </summary>
    public static TimeOnly? ConvertEpochToTime(long epochMilliseconds) {
        try {
            return TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(epochMilliseconds));
        } catch {
            return null;
        }
    }
}
