namespace SalesforceGrpc.Extensions;

/// <summary>
/// Handles type conversion for different Avro field types
/// Uses the schema doc property to identify datetime fields for proper SQL conversion
/// </summary>
public static class FieldTypeConverter {
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Converts a value based on its Avro type and field documentation
    /// The doc property from the schema is more reliable than the type for identifying datetime fields
    /// </summary>
    public static object? ConvertValue(object? value, string avroType, string? fieldDoc = null) {
        if (value == null) {
            return null;
        }

        // Check doc property first for accurate type identification
        // Schema doc format examples: "Data:DateTime", "CreatedDate:DateTime", "Data:Date", etc.
        if (!string.IsNullOrEmpty(fieldDoc)) {
            if (IsDateTimeField(fieldDoc)) {
                return ConvertEpochToDateTime(Convert.ToInt64(value));
            }

            if (IsDateOnlyField(fieldDoc)) {
                return ConvertEpochToDate(Convert.ToInt64(value));
            }

            if (IsTimeOnlyField(fieldDoc)) {
                return ConvertEpochToTime(Convert.ToInt64(value));
            }
        }

        // Fallback to type-based detection if no doc property
        var normalizedType = avroType.Split('|')[0].Trim().ToLowerInvariant();

        return normalizedType switch {
            "long" => Convert.ToInt64(value),
            "int" => Convert.ToInt32(value),
            "double" => Convert.ToDouble(value),
            "float" => Convert.ToSingle(value),
            "boolean" => Convert.ToBoolean(value),
            _ => $"{EscapeSqlString(value.ToString() ?? "")}"
        };
    }

    /// <summary>
    /// Converts epoch milliseconds to SQL-compatible datetime string
    /// Format: '2024-01-15 10:30:45.000'
    /// </summary>
    private static DateTime? ConvertEpochToDateTime(long epochMilliseconds) {
        try {
            var dateTime = UnixEpoch.AddMilliseconds(epochMilliseconds);
            return dateTime;
            // return $"'{dateTime:yyyy-MM-dd HH:mm:ss.fff}'";
        } catch {
            // return "NULL";
            return null;
        }
    }

    /// <summary>
    /// Converts epoch milliseconds to SQL-compatible date string
    /// Format: '2024-01-15'
    /// </summary>
    private static DateTime? ConvertEpochToDate(long epochMilliseconds) {
        try {
            var date = UnixEpoch.AddMilliseconds(epochMilliseconds);
            return date;
            // return $"'{dateTime:yyyy-MM-dd}'";
        } catch {
            // return "NULL";
            return null;
        }
    }

    /// <summary>
    /// Converts epoch milliseconds to SQL-compatible time string
    /// Format: '10:30:45.000'
    /// </summary>
    public static TimeOnly? ConvertEpochToTime(long epochMilliseconds) {
        try {
            // var timeOnly = new TimeOnly(epochMilliseconds);
            // var dateTime = UnixEpoch.AddMilliseconds(epochMilliseconds);
            // return $"'{dateTime:HH:mm:ss.fff}'";
            var fromDateTime = TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(epochMilliseconds));
            return fromDateTime;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Checks if the doc field indicates a DateTime type
    /// Examples: "Data:DateTime", "CreatedDate:DateTime", "Data:DateTime:fieldId"
    /// </summary>
    private static bool IsDateTimeField(string fieldDoc) {
        return fieldDoc.Contains("DateTime", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the doc field indicates a Date (DateOnly) type
    /// Examples: "Data:Date", "BirthDate:Date"
    /// </summary>
    private static bool IsDateOnlyField(string fieldDoc) {
        return fieldDoc.Contains(":Date", StringComparison.OrdinalIgnoreCase) &&
               !fieldDoc.Contains("DateTime", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the doc field indicates a Time (TimeOnly) type
    /// Examples: "Data:Time", "BusinessHours:Time"
    /// </summary>
    private static bool IsTimeOnlyField(string fieldDoc) {
        return fieldDoc.Contains(":Time", StringComparison.OrdinalIgnoreCase) &&
               !fieldDoc.Contains("DateTime", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Escapes single quotes in string values for SQL safety
    /// Returns only the escaped string without quotes - quotes should be added by the caller
    /// </summary>
    private static string EscapeSqlString(string value) {
        return value.Replace("'", "''");
    }
}