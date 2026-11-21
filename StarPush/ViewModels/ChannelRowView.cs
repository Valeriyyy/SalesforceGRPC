namespace StarPush.ViewModels;

/// <summary>One Channel on the list. <see cref="Streaming"/> is set for the Primary Channel only.</summary>
public sealed record ChannelRowView(
    int Id,
    string Label,
    string FullName,
    bool IsPrimary,
    int MemberCount,
    int UnboundCount,
    string? Streaming);
