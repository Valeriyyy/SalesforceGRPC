using Database.Models;

namespace StarPush.ViewModels;

/// <summary>An Inbound Failure: a change event that was skipped, so the change is missing from the Target Table.</summary>
/// <param name="ChangeType">Null when the event's header could not be read.</param>
/// <param name="RecordIds">Empty when the event could not be decoded far enough to say.</param>
public sealed record InboundFailureView(
    int Id,
    string? ChangeType,
    IReadOnlyList<string> RecordIds,
    string Message,
    DateTime FirstFailedAt,
    DateTime LastFailedAt,
    int Attempts) {

    public static InboundFailureView For(InboundFailure failure) => new(
        failure.Id,
        failure.ChangeType,
        failure.RecordIds,
        failure.Message,
        DateTime.SpecifyKind(failure.FirstFailedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(failure.LastFailedAt, DateTimeKind.Utc),
        failure.Attempts);
}
