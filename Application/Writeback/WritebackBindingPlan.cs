using Database.Writeback;

namespace Application.Writeback;

/// <summary>
/// One Two-way Binding, resolved into what Writeback needs per change: no further lookups at run time.
/// </summary>
public sealed class WritebackBindingPlan {
    public required int BindingId { get; init; }
    public required string EntityName { get; init; }

    /// <summary>The Salesforce object records are written to, e.g. <c>Account</c>.</summary>
    public required string SourceObject { get; init; }

    public required WritebackTable Table { get; init; }

    /// <summary>The Target Table name as the repository addresses it, e.g. <c>salesforce.account</c>.</summary>
    public required string TableName { get; init; }

    /// <summary>The column holding the Salesforce record ID. Written by Writeback, never sent as a field.</summary>
    public required string KeyMappingColumn { get; init; }

    /// <summary>The Writeback Key: the column that identifies a row, and the External ID field that stores it.</summary>
    public required WritebackField WritebackKey { get; init; }

    /// <summary>The fields whose effective Field Direction carries Writeback, each named as the REST API knows it.</summary>
    public required IReadOnlyList<WritebackField> Fields { get; init; }

    /// <summary>Writeback Defaults, already converted to the values sent, keyed by Salesforce field.</summary>
    public IReadOnlyList<WritebackDefaultValue> Defaults { get; init; } = [];
}
