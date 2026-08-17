namespace Salesforce.Avro;

/// <summary>
/// A parsed Avro <c>doc</c> annotation from a Salesforce change event field.
/// </summary>
/// <remarks>
/// Salesforce formats these as <c>&lt;role&gt;:&lt;type&gt;[:&lt;field id&gt;]</c> — for example
/// <c>Data:DateOnly:00NDp000009Rr9I</c>, <c>ForeignKey:EntityId</c> or <c>CreatedDate:DateTime</c>. This is
/// the single place that reading is done, so the Field Type a user validated a mapping against and the Field
/// Type the worker converts a value with cannot diverge.
/// </remarks>
public readonly record struct SalesforceFieldDoc {
    /// <summary>The first segment — "Data", "ForeignKey", or the field's own name for audit fields.</summary>
    public string Role { get; private init; }

    /// <summary>The second segment exactly as Salesforce wrote it, kept so an unrecognised type is visible.</summary>
    public string RawType { get; private init; }

    public SalesforceFieldType FieldType { get; private init; }

    /// <summary>The optional third segment, the Salesforce field ID. Null for standard fields.</summary>
    public string? FieldId { get; private init; }

    private static readonly Dictionary<string, SalesforceFieldType> TypesByName =
        new(StringComparer.OrdinalIgnoreCase) {
            ["Text"] = SalesforceFieldType.Text,
            ["StringPlusClob"] = SalesforceFieldType.StringPlusClob,
            ["Email"] = SalesforceFieldType.Email,
            ["Url"] = SalesforceFieldType.Url,
            ["Phone"] = SalesforceFieldType.Phone,
            ["EntityId"] = SalesforceFieldType.EntityId,
            ["ExternalId"] = SalesforceFieldType.ExternalId,
            ["DynamicEnum"] = SalesforceFieldType.DynamicEnum,
            ["StaticEnum"] = SalesforceFieldType.StaticEnum,
            ["MultiEnum"] = SalesforceFieldType.MultiEnum,
            ["Boolean"] = SalesforceFieldType.Boolean,
            ["Integer"] = SalesforceFieldType.Integer,
            ["Double"] = SalesforceFieldType.Double,
            ["Currency"] = SalesforceFieldType.Currency,
            ["Percent"] = SalesforceFieldType.Percent,
            ["DateTime"] = SalesforceFieldType.DateTime,
            ["DateOnly"] = SalesforceFieldType.DateOnly,
            ["TimeOnly"] = SalesforceFieldType.TimeOnly,
            ["Address"] = SalesforceFieldType.Address,
            ["Location"] = SalesforceFieldType.Location,
            ["PersonName"] = SalesforceFieldType.PersonName,
            ["ComplexValueType"] = SalesforceFieldType.ComplexValueType
        };

    /// <summary>
    /// Salesforce prefixes a compound type with "Switchable_" when the field can arrive either as that record
    /// or as a plain scalar — Account.Name is <c>Data:Switchable_PersonName</c> because a person account
    /// carries a structured name where a business account carries a string.
    /// </summary>
    private const string SwitchablePrefix = "Switchable_";

    public static SalesforceFieldDoc Parse(string? doc) {
        if (string.IsNullOrWhiteSpace(doc)) {
            return new SalesforceFieldDoc { Role = "", RawType = "", FieldType = SalesforceFieldType.Unknown };
        }

        var segments = doc.Split(':');

        // A single segment carries no role, so read it as the type rather than discarding it.
        var role = segments.Length > 1 ? segments[0].Trim() : "";
        var rawType = (segments.Length > 1 ? segments[1] : segments[0]).Trim();
        var fieldId = segments.Length > 2 && !string.IsNullOrWhiteSpace(segments[2]) ? segments[2].Trim() : null;

        var lookup = rawType.StartsWith(SwitchablePrefix, StringComparison.OrdinalIgnoreCase)
            ? rawType[SwitchablePrefix.Length..]
            : rawType;

        return new SalesforceFieldDoc {
            Role = role,
            RawType = rawType,
            FieldId = fieldId,
            FieldType = TypesByName.GetValueOrDefault(lookup, SalesforceFieldType.Unknown)
        };
    }
}
