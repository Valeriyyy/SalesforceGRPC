using Database.Models;

namespace Application.Runtime;

/// <summary>
/// Turns what the workers report into Incidents. Never throws: recording an Incident must not stop syncing.
/// </summary>
public interface IIncidentRecorder {
    /// <summary>
    /// Tells the recorder a direction's Run State. Called on every report, repeats included: a repeated Retrying
    /// is another retry inside the same Incident.
    /// </summary>
    Task ReportAsync(RunDirection direction, RunState state, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Records that Salesforce rejected a Checkpoint and the changes since it are lost.</summary>
    Task CheckpointDiscardedAsync(string message, CancellationToken cancellationToken = default);

    Task StartedAsync(CancellationToken cancellationToken = default);

    Task StoppedAsync(CancellationToken cancellationToken = default);
}
