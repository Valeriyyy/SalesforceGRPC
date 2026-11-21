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
