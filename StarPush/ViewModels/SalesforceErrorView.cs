using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// A Salesforce failure, both halves: what it most likely means, and Salesforce's own words.
/// </summary>
/// <remarks>Guidance is null when the error is not recognised; the page says so rather than guessing.</remarks>
public sealed record SalesforceErrorView(string Error, string ErrorDescription, string? Guidance, string RawResponse,
    DateTime? OccurredAt) {

    public static SalesforceErrorView? From(OAuthFailureDTO? failure) => failure is null
        ? null
        : new(failure.Error, failure.ErrorDescription, failure.Guidance, failure.RawResponse, failure.OccurredAt);
}
