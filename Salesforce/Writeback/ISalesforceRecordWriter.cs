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
