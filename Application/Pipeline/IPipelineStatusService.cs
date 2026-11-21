namespace Application.Pipeline;

/// <summary>
/// Answers what each Stage of the Pipeline's status is, and why, as domain data.
/// </summary>
/// <remarks>
/// The one place the Stage Status rules live, so the shell, the Overview and any later API agree on them. Reads
/// the repositories directly rather than the cached providers: a page load has to show current state.
/// </remarks>
public interface IPipelineStatusService {
    Task<PipelineStatus> GetAsync(CancellationToken cancellationToken = default);
}
