using System.Globalization;

namespace Application.Writeback;

/// <summary>
/// Turns a Target Column's value into the JSON value Salesforce expects for a field, by the field's describe
/// type. The reverse of the inbound field conversion.
/// </summary>
/// <remarks>
/// Driven by the describe type rather than the Avro Field Type, because the describe is what the REST API
/// validates against. Values arrive as the engine's driver reads them (DateTime, decimal, TimeSpan, ...).
/// </remarks>
public static class WritebackValueConverter {

    public static object? ToSalesforce(object? value, string salesforceType) {
        if (value is null or DBNull) {
            return null;
        }

        return salesforceType.ToLowerInvariant() switch {
            "date" => FormatDate(value),
            "datetime" => FormatDateTime(value),
            "time" => FormatTime(value),
            "double" or "currency" or "percent" => ToDecimal(value),
            "int" => Convert.ToInt64(ToDecimal(value), CultureInfo.InvariantCulture),
            "long" => Convert.ToInt64(ToDecimal(value), CultureInfo.InvariantCulture),
            "boolean" => ToBoolean(value),
            _ => FormatText(value)
        };
    }

    /// <summary>
    /// Turns a Writeback Default, stored as the text the user typed, into the value sent. Throws a
    /// <see cref="FormatException"/> naming the problem when the text cannot be a value of that type.
    /// </summary>
    public static object? ParseDefault(string text, string salesforceType) {
        switch (salesforceType.ToLowerInvariant()) {
            case "double" or "currency" or "percent":
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : throw new FormatException($"'{text}' is not a number.");
            case "int" or "long":
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    ? whole
                    : throw new FormatException($"'{text}' is not a whole number.");
            case "boolean":
                return bool.TryParse(text, out var flag)
                    ? flag
                    : throw new FormatException($"'{text}' is not true or false.");
            case "date":
                return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? text
                    : throw new FormatException($"'{text}' is not a date in the form yyyy-MM-dd.");
            case "datetime":
                return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                    ? FormatDateTime(at)
                    : throw new FormatException($"'{text}' is not a date and time.");
            default:
                return text;
        }
    }

    private static string FormatDate(object value) => value switch {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => FormatText(value)
    };

    /// <remarks>
    /// A timestamp without a time zone is taken to be UTC, which is how the inbound flow writes Salesforce's
    /// DateTimes into it.
    /// </remarks>
    private static string FormatDateTime(object value) => value switch {
        DateTime { Kind: DateTimeKind.Local } local => FormatUtc(local.ToUniversalTime()),
        DateTime dateTime => FormatUtc(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        DateTimeOffset offset => FormatUtc(offset.UtcDateTime),
        DateOnly date => FormatUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
        _ => FormatText(value)
    };

    private static string FormatUtc(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string FormatTime(object value) => value switch {
        TimeSpan span => TimeOnly.FromTimeSpan(span).ToString("HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        _ => FormatText(value)
    };

    private static decimal ToDecimal(object value) => value switch {
        decimal d => d,
        string s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture),
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
    };

    private static bool ToBoolean(object value) => value switch {
        bool b => b,
        string s => bool.Parse(s),
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0
    };

    private static string FormatText(object value) => value switch {
        string s => s,
        bool b => b ? "true" : "false",
        DateTime dateTime => FormatDateTime(dateTime),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
