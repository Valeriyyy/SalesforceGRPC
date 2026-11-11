using Newtonsoft.Json;

namespace Salesforce.Writeback;

/// <summary>
/// The Salesforce writes Writeback makes: updates of records by their ID, and upserts on an External ID field.
/// </summary>
/// <remarks>
/// Each call takes up to <see cref="MaxRecordsPerCall"/> records and is never all-or-none, so one refused
/// record does not take the others down with it. A result is returned per record, in request order.
/// </remarks>
public interface ISalesforceRecordWriter {
    /// <summary>The most records one sObject Collections call accepts.</summary>
    const int MaxRecordsPerCall = 200;

    /// <summary>Updates records by ID. Every record must carry its <c>Id</c>.</summary>
    /// <exception cref="SalesforceTransientException">Nothing was decided; the same call may be made again.</exception>
    /// <exception cref="SalesforceUnauthorizedException">The Run-as User's session was refused.</exception>
    Task<IReadOnlyList<RecordSaveResult>> UpdateAsync(string sObject,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates records matched on an External ID field. Every record must carry that field.
    /// </summary>
    /// <exception cref="SalesforceTransientException">Nothing was decided; the same call may be made again.</exception>
    /// <exception cref="SalesforceUnauthorizedException">The Run-as User's session was refused.</exception>
    Task<IReadOnlyList<RecordSaveResult>> UpsertAsync(string sObject, string externalIdField,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records, CancellationToken cancellationToken = default);
}

/// <summary>What Salesforce said about one record of a collection call.</summary>
public sealed class RecordSaveResult {
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("success")]
    public bool Success { get; set; }

    /// <summary>For an upsert, whether the record was created rather than matched.</summary>
    [JsonProperty("created")]
    public bool Created { get; set; }

    [JsonProperty("errors")]
    public List<RecordError> Errors { get; set; } = [];
}

public sealed class RecordError {
    [JsonProperty("statusCode")]
    public string StatusCode { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    [JsonProperty("fields")]
    public List<string> Fields { get; set; } = [];

    /// <summary>
    /// The status codes that say "not now" rather than "no": the same record may succeed if sent again.
    /// </summary>
    public static readonly IReadOnlySet<string> TransientStatusCodes = new HashSet<string>(StringComparer.Ordinal) {
        "UNABLE_TO_LOCK_ROW", "REQUEST_LIMIT_EXCEEDED", "SERVER_UNAVAILABLE", "REQUEST_RUNNING_TOO_LONG"
    };

    [JsonIgnore]
    public bool IsTransient => TransientStatusCodes.Contains(StatusCode);
}

/// <summary>
/// A Salesforce call that failed for a reason that may pass (network, outage, API limits, a locked row). The
/// call decided nothing and may be made again.
/// </summary>
public sealed class SalesforceTransientException : Exception {
    public SalesforceTransientException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Salesforce refused the Run-as User's session. Retrying will not help until the Org Connection is fixed.</summary>
public sealed class SalesforceUnauthorizedException : Exception {
    public SalesforceUnauthorizedException(string message) : base(message) { }
}

/// <summary>Reads a Source Object's field list as the REST describe reports it.</summary>
public interface ISObjectDescriber {
    /// <summary>The describe of an object, e.g. <c>Account</c>, or null when the object does not exist.</summary>
    Task<SObjectDescribe?> DescribeAsync(string sObject, CancellationToken cancellationToken = default);
}

/// <summary>The parts of a Source Object describe Writeback reads.</summary>
public sealed class SObjectDescribe {
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("fields")]
    public List<SObjectField> Fields { get; set; } = [];

    public SObjectField? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One field of a Source Object describe.</summary>
public sealed class SObjectField {
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("label")]
    public string? Label { get; set; }

    /// <summary>The describe type: string, double, currency, date, datetime, time, boolean, picklist, reference...</summary>
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("updateable")]
    public bool Updateable { get; set; }

    [JsonProperty("createable")]
    public bool Createable { get; set; }

    [JsonProperty("nillable")]
    public bool Nillable { get; set; }

    [JsonProperty("defaultedOnCreate")]
    public bool DefaultedOnCreate { get; set; }

    [JsonProperty("externalId")]
    public bool ExternalId { get; set; }

    [JsonProperty("unique")]
    public bool Unique { get; set; }

    [JsonProperty("calculated")]
    public bool Calculated { get; set; }

    [JsonProperty("length")]
    public int Length { get; set; }

    [JsonProperty("precision")]
    public int Precision { get; set; }

    [JsonProperty("scale")]
    public int Scale { get; set; }

    [JsonProperty("restrictedPicklist")]
    public bool RestrictedPicklist { get; set; }

    [JsonProperty("picklistValues")]
    public List<SObjectPicklistValue> PicklistValues { get; set; } = [];

    /// <summary>The compound field this one is part of (BillingAddress for BillingStreet), or null.</summary>
    [JsonProperty("compoundFieldName")]
    public string? CompoundFieldName { get; set; }

    /// <summary>True when Writeback may send a value for it at all.</summary>
    [JsonIgnore]
    public bool IsWritable => !Calculated && (Updateable || Createable);

    /// <summary>
    /// True when a record cannot be created without a value for it: Salesforce neither accepts null nor fills
    /// one in. A boolean always has a value, so it is never one of these.
    /// </summary>
    [JsonIgnore]
    public bool IsRequiredOnCreate => Createable && !Nillable && !DefaultedOnCreate && Type != "boolean";
}

public sealed class SObjectPicklistValue {
    [JsonProperty("value")]
    public string Value { get; set; } = "";

    [JsonProperty("active")]
    public bool Active { get; set; }
}
