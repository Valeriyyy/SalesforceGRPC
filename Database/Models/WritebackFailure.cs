namespace Database.Models;

/// <summary>
/// A Target Table change that Salesforce refused, recorded for the user and skipped.
/// </summary>
/// <remarks>
/// One per row of a Binding: <see cref="RowKey"/> names the row, and a later failure for the same row replaces
/// this one. A later success for the row clears it.
/// </remarks>
public class WritebackFailure {
    public int Id { get; set; }
    public int BindingId { get; set; }

    /// <summary>
    /// The row's identity, built from whichever keys it had, so the same row always gets the same key.
    /// </summary>
    public required string RowKey { get; set; }

    public string? WritebackKeyValue { get; set; }
    public string? SalesforceId { get; set; }

    public WritebackOperation Operation { get; set; }

    /// <summary>Salesforce's status codes, comma-separated (FIELD_CUSTOM_VALIDATION_EXCEPTION, ...).</summary>
    public required string StatusCodes { get; set; }

    public required string Message { get; set; }

    /// <summary>The field values that were sent, as JSON, so the user can see what Salesforce refused.</summary>
    public string ValuesSent { get; set; } = "{}";

    public DateTime FirstFailedAt { get; set; }
    public DateTime LastFailedAt { get; set; }
    public int Attempts { get; set; } = 1;

    /// <summary>
    /// The row key for a row: the Writeback Key value when it has one, otherwise its Salesforce record ID.
    /// </summary>
    /// <remarks>
    /// The Writeback Key wins because it never changes for a row, while the Salesforce ID appears only once the
    /// row has reached Salesforce. Prefixed so the two kinds can never collide.
    /// </remarks>
    public static string RowKeyFor(string? writebackKeyValue, string? salesforceId) =>
        writebackKeyValue is not null ? $"key:{writebackKeyValue}" : $"id:{salesforceId}";
}
