namespace StarPush.ViewModels;

/// <summary>The Incidents page: everything that interrupted syncing in the last 24 hours, latest first.</summary>
public sealed record IncidentsView(IReadOnlyList<IncidentView> Incidents);
