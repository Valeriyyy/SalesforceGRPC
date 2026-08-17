namespace Salesforce.Avro;

/// <summary>
/// The semantic Salesforce type of one Entity field.
/// </summary>
/// <remarks>
/// Salesforce does not express this in the Avro type system — it emits no logical types, so Date, DateTime and
/// Time all arrive as <c>long</c>, and Currency, Percent, Number and Double all as <c>double</c>. The
/// distinction survives only in each field's doc annotation, which <see cref="SalesforceFieldDoc"/> reads.
/// </remarks>
public enum SalesforceFieldType {
    /// <summary>No doc annotation, or a type Salesforce introduced after this enum was written.</summary>
    Unknown = 0,

    // String-shaped
    Text,
    StringPlusClob,
    Email,
    Url,
    Phone,
    EntityId,
    ExternalId,
    DynamicEnum,
    StaticEnum,
    MultiEnum,

    // Scalar
    Boolean,
    Integer,
    Double,
    Currency,
    Percent,

    // Temporal — all three arrive as an Avro long
    DateTime,
    DateOnly,
    TimeOnly,

    // Compound — arrive as a nested Avro record and are flattened before mapping
    Address,
    Location,
    PersonName,
    ComplexValueType
}

public static class SalesforceFieldTypeExtensions {
    /// <summary>True for the types that share the Avro <c>long</c> and differ only by doc annotation.</summary>
    public static bool IsTemporal(this SalesforceFieldType type) => type is
        SalesforceFieldType.DateTime or SalesforceFieldType.DateOnly or SalesforceFieldType.TimeOnly;

    public static bool IsNumeric(this SalesforceFieldType type) => type is
        SalesforceFieldType.Integer or SalesforceFieldType.Double or
        SalesforceFieldType.Currency or SalesforceFieldType.Percent;

    public static bool IsBoolean(this SalesforceFieldType type) => type is SalesforceFieldType.Boolean;

    /// <summary>
    /// True for the types that arrive as a nested Avro record. A compound field has no single value to write,
    /// so mapping one as a whole is always an error — its flattened parts are what can be mapped.
    /// </summary>
    public static bool IsCompound(this SalesforceFieldType type) => type is
        SalesforceFieldType.Address or SalesforceFieldType.Location or
        SalesforceFieldType.PersonName or SalesforceFieldType.ComplexValueType;

    /// <summary>True for the types whose value is written as text.</summary>
    public static bool IsTextual(this SalesforceFieldType type) => type is
        SalesforceFieldType.Text or SalesforceFieldType.StringPlusClob or SalesforceFieldType.Email or
        SalesforceFieldType.Url or SalesforceFieldType.Phone or SalesforceFieldType.EntityId or
        SalesforceFieldType.ExternalId or SalesforceFieldType.DynamicEnum or
        SalesforceFieldType.StaticEnum or SalesforceFieldType.MultiEnum;
}
