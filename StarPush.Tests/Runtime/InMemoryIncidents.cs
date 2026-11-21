using Database.Models;
using Database.Repositories.Interfaces;

namespace StarPush.Tests.Runtime;

/// <summary>An Incident store that keeps everything in a list, so a test can read back what was recorded.</summary>
internal sealed class InMemoryIncidents : IIncidentRepository {
    private readonly List<Incident> _incidents = [];
    private int _nextId = 1;

    /// <summary>Makes every call throw, to prove a failing store never stops a worker.</summary>
    public bool Unavailable { get; set; }

    public IReadOnlyList<Incident> All {
        get { lock (_incidents) { return _incidents.Select(Copy).ToList(); } }
    }

    public Task<int> OpenAsync(Incident incident, CancellationToken cancellationToken = default) {
        lock (_incidents) {
            ThrowIfUnavailable();
            var stored = Copy(incident);
            stored.Id = _nextId++;
            _incidents.Add(stored);
            return Task.FromResult(stored.Id);
        }
    }

    public Task ContinueAsync(int incidentId, string message, CancellationToken cancellationToken = default) {
        lock (_incidents) {
            ThrowIfUnavailable();
            var incident = _incidents.Single(i => i.Id == incidentId);
            incident.Occurrences++;
            incident.Message = message;
            incident.EndedAt = null;
        }
        return Task.CompletedTask;
    }

    public Task CloseAsync(int incidentId, DateTime endedAt, CancellationToken cancellationToken = default) {
        lock (_incidents) {
            ThrowIfUnavailable();
            _incidents.Single(i => i.Id == incidentId).EndedAt = endedAt;
        }
        return Task.CompletedTask;
    }

    public Task CloseAllOpenAsync(DateTime endedAt, CancellationToken cancellationToken = default) {
        lock (_incidents) {
            ThrowIfUnavailable();
            foreach (var incident in _incidents.Where(i => i.EndedAt is null)) {
                incident.EndedAt = endedAt;
            }
        }
        return Task.CompletedTask;
    }

    public Task<List<Incident>> ListSinceAsync(DateTime since, CancellationToken cancellationToken = default) {
        lock (_incidents) {
            ThrowIfUnavailable();
            return Task.FromResult(_incidents
                .Where(i => i.StartedAt >= since || i.EndedAt is null || i.EndedAt >= since)
                .OrderByDescending(i => i.StartedAt).ThenByDescending(i => i.Id)
                .Select(Copy).ToList());
        }
    }

    public Task<IncidentKind?> GetLatestLifecycleKindAsync(CancellationToken cancellationToken = default) {
        lock (_incidents) {
            ThrowIfUnavailable();
            return Task.FromResult(_incidents
                .Where(i => i.Kind is IncidentKind.Started or IncidentKind.Stopped)
                .OrderByDescending(i => i.StartedAt).ThenByDescending(i => i.Id)
                .Select(i => (IncidentKind?)i.Kind).FirstOrDefault());
        }
    }

    private void ThrowIfUnavailable() {
        if (Unavailable) {
            throw new InvalidOperationException("App Database unavailable");
        }
    }

    private static Incident Copy(Incident i) => new() {
        Id = i.Id, Direction = i.Direction, Kind = i.Kind, Message = i.Message,
        StartedAt = i.StartedAt, EndedAt = i.EndedAt, Occurrences = i.Occurrences
    };
}
