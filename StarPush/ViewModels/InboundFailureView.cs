using Database.Models;
using DTO;

namespace StarPush.ViewModels;

/// <summary>An Inbound Failure: a change event that was skipped, so the change is missing from the Target Table.</summary>
/// <param name="ChangeType">Null when the event's header could not be read.</param>
/// <param name="RecordIds">Empty when the event could not be decoded far enough to say.</param>
/// <param name="Problem">What went wrong, in the Binding's terms.</param>
/// <param name="Steps">What to do, in order; the last is always correcting the skipped record.</param>
/// <param name="Message">The error exactly as it was raised, for the details.</param>
/// <param name="ValuesSent">The column values that were being written, as JSON; "{}" when none reached the database.</param>
public sealed record InboundFailureView(
    int Id,
    string? ChangeType,
    IReadOnlyList<string> RecordIds,
    string Problem,
    IReadOnlyList<string> Steps,
    string Message,
    string ValuesSent,
    DateTime FirstFailedAt,
    DateTime LastFailedAt,
    int Attempts) {

    /// <param name="columns">The Target Table's columns, when the Target Database could be read for the page.</param>
    /// <param name="bindingTable">The Binding's Target Table, for failures recorded before their table was kept.</param>
    public static InboundFailureView For(InboundFailure failure, IReadOnlyList<TargetColumnDTO>? columns = null,
        string? bindingTable = null, IReadOnlyList<BindableFieldDTO>? fields = null) {
        var explanation = InboundFailureExplanation.For(failure, columns, bindingTable, fields);
        return new(
            failure.Id,
            failure.ChangeType,
            failure.RecordIds,
            explanation.Problem,
            explanation.Steps,
            failure.Message,
            failure.ValuesSent,
            DateTime.SpecifyKind(failure.FirstFailedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(failure.LastFailedAt, DateTimeKind.Utc),
            failure.Attempts);
    }
}
