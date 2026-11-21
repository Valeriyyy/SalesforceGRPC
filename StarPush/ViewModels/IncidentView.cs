using Database.Models;

namespace StarPush.ViewModels;

/// <summary>One Incident. <see cref="EndedAt"/> is null while it is still going on.</summary>
/// <param name="Direction">Null for the application starting or stopping.</param>
public sealed record IncidentView(
    int Id,
    RunDirection? Direction,
    IncidentKind Kind,
    string Message,
    DateTime StartedAt,
    DateTime? EndedAt,
    int Occurrences) {

    public static IncidentView For(Incident incident) => new(
        incident.Id,
        incident.Direction,
        incident.Kind,
        incident.Message,
        DateTime.SpecifyKind(incident.StartedAt, DateTimeKind.Utc),
        incident.EndedAt is { } ended ? DateTime.SpecifyKind(ended, DateTimeKind.Utc) : null,
        incident.Occurrences);
}
