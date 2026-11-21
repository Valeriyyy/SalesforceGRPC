using Application.Runtime;

namespace StarPush.Runtime;

/// <summary>
/// Records the application starting and stopping as Incidents. Registered before the workers, so it starts
/// first and stops last.
/// </summary>
/// <remarks>
/// Starting is recorded in the background: an App Database that is down must not hold up startup, since the API
/// this host serves is how the user fixes things. The recorder takes one change at a time, so the "started"
/// Incident still lands before anything a worker reports.
/// </remarks>
public sealed class LifecycleIncidents(IIncidentRecorder incidents) : IHostedService {
    public Task StartAsync(CancellationToken cancellationToken) {
        _ = incidents.StartedAsync(CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => incidents.StoppedAsync(cancellationToken);
}
