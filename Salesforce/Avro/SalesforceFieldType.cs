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
