namespace StarPush.ViewModels;

public sealed record ChannelMemberView(
    int Id,
    string Entity,
    string? FilterExpression,
    IReadOnlyList<string> EnrichedFields,
    MemberBindingView? Binding);
