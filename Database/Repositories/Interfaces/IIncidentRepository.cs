using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>The Incidents of the last 24 hours. Older ones are removed as new ones are opened.</summary>
public interface IIncidentRepository {
    /// <summary>How long an Incident is kept after it ended. One still going on is kept however old it is.</summary>
    static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>Stores a new Incident and returns its id, removing every one that ended more than 24 hours ago.</summary>
    Task<int> OpenAsync(Incident incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Carries an Incident on: counts one more occurrence, takes the latest message, and marks it going on again
    /// if it had ended.
    /// </summary>
    Task ContinueAsync(int incidentId, string message, CancellationToken cancellationToken = default);

    Task CloseAsync(int incidentId, DateTime endedAt, CancellationToken cancellationToken = default);

    /// <summary>Ends every Incident still going on, as of <paramref name="endedAt"/>.</summary>
    Task CloseAllOpenAsync(DateTime endedAt, CancellationToken cancellationToken = default);

    /// <summary>Every Incident that started, or was still going on, since <paramref name="since"/>; latest first.</summary>
    Task<List<Incident>> ListSinceAsync(DateTime since, CancellationToken cancellationToken = default);

    /// <summary>The kind of the latest Started or Stopped Incident, or null when none is kept.</summary>
    Task<IncidentKind?> GetLatestLifecycleKindAsync(CancellationToken cancellationToken = default);
}
