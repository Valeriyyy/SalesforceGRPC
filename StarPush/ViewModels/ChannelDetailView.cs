using Database.Models;

namespace StarPush.ViewModels;

public sealed record ChannelDetailView(
    int Id,
    string Label,
    string FullName,
    bool IsPrimary,
    StartingPoint StartingPoint,
    CheckpointView? Checkpoint,
    IReadOnlyList<ChannelMemberView> Members);
