namespace StarPush.ViewModels;

/// <summary>Both org ids, because that is the first thing anyone asks when a connection is refused for one.</summary>
public sealed record OrgMismatchView(string StoredOrgId, string DiscoveredOrgId);
