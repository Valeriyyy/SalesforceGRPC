namespace Application.Pipeline;

public sealed record OrgConnectionStage : StageState {
    public override PipelineStage Stage => PipelineStage.OrgConnection;
    public string? RunAsUsername { get; init; }
    public string? OrgUrl { get; init; }
    public string? OrgId { get; init; }
    public DateTime? LastConnectedAt { get; init; }
}
