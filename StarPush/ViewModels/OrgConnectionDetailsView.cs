namespace StarPush.ViewModels;

/// <summary>What the user typed, shown back to them. The Consumer Secret is never part of it.</summary>
public sealed record OrgConnectionDetailsView(string ConsumerKey, string AdministeringUsername, string RunAsUsername, bool IsSandbox);
