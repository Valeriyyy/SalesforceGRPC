using Database.Models;

namespace Application.Pipeline;

public sealed record TargetConnectionStage : StageState {
    public override PipelineStage Stage => PipelineStage.TargetConnection;
    public TargetDatabaseEngine? Engine { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? DatabaseName { get; init; }
    public string? FilePath { get; init; }
    public DateTime? LastConnectedAt { get; init; }
}
