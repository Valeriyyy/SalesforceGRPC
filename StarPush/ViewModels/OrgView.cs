namespace StarPush.ViewModels;

/// <summary>
/// The org this installation is bound to. The org id outlives an edit of the details; the URL and the last
/// success do not.
/// </summary>
public sealed record OrgView(string OrgId, string? OrgUrl, DateTime? LastConnectedAt);
