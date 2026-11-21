namespace Application.Runtime;

/// <summary>
/// Answers what the workers are doing and what has happened lately. Read on every page load of the Overview and
/// on every poll of its worker card, so it reads the in-memory Run States and three small counts, nothing more.
/// </summary>
public interface IWorkerStatusService {
    Task<WorkerStatus> GetAsync(CancellationToken cancellationToken = default);
}
