using Database.Repositories.Interfaces;

namespace Application.Pipeline;

public sealed record BindingsStage : StageState {
    public override PipelineStage Stage => PipelineStage.Bindings;
    public BindingStateCounts Counts { get; init; } = new();

    /// <summary>Target Table changes Salesforce refused and nobody has retried or dismissed yet.</summary>
    public int WritebackFailures { get; init; }

    /// <summary>How far Writeback is behind the Target Database, in bytes, when last measured.</summary>
    public long? WritebackLagBytes { get; init; }

    /// <summary>Why Writeback cannot run although a Binding is Two-way, or null.</summary>
    public string? WritebackBlockedReason { get; init; }
}
