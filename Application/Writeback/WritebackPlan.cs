using Database.Writeback;

namespace Application.Writeback;

/// <summary>
/// What the Writeback worker should do: which Two-way Bindings to write back, and against which database.
/// </summary>
/// <remarks>
/// Decided by the Binding service, which re-validates every Two-way Binding first, the same way the inbound
/// subscription plan re-checks Key Mapping uniqueness. The worker only runs what the plan says.
/// </remarks>
public sealed class WritebackPlan {
    /// <summary>The Two-way Active Bindings to write back. Empty means Writeback has nothing to do.</summary>
    public IReadOnlyList<WritebackBindingPlan> Bindings { get; init; } = [];

    /// <summary>The engine's Writeback, or null when the Target Database Engine does not support it.</summary>
    public IWritebackCapability? Capability { get; init; }

    /// <summary>The logins Writeback uses, or null when there is no usable Replication Credential.</summary>
    public WritebackEndpoints? Endpoints { get; init; }

    /// <summary>Why Writeback cannot run although Bindings want it, or null when it can.</summary>
    public string? BlockedReason { get; init; }

    public bool HasWork => Bindings.Count > 0;

    public bool CanRun => HasWork && Capability is not null && Endpoints is not null && BlockedReason is null;

    public static WritebackPlan Empty { get; } = new();

    /// <summary>The Binding writing to a table, matched the way the engine names tables.</summary>
    public WritebackBindingPlan? BindingFor(WritebackTable table) =>
        Bindings.FirstOrDefault(b => b.Table == table);
}

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

/// <summary>
/// A Target Column and the Salesforce field it is written to, with that field's describe type, which decides
/// how the column's value is sent.
/// </summary>
public sealed record WritebackField(string Column, string SalesforceField, string SalesforceType);

/// <summary>A Writeback Default, converted to the value sent when a record is created.</summary>
public sealed record WritebackDefaultValue(string SalesforceField, object? Value);
