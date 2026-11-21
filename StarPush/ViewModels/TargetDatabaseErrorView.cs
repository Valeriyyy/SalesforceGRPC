using DTO;

namespace StarPush.ViewModels;

/// <summary>A Target Database failure: the summary, and the driver's own words.</summary>
public sealed record TargetDatabaseErrorView(string Message, string RawResponse, DateTime? OccurredAt) {
    public static TargetDatabaseErrorView? From(TargetConnectionFailureDTO? failure) => failure is null
        ? null
        : new(failure.Message, failure.RawResponse, failure.OccurredAt);
}
