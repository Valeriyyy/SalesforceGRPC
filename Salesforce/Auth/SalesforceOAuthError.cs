namespace Salesforce.Auth;

/// <summary>
/// A rejected OAuth request, as both Salesforce said it and as a user can act on it.
/// </summary>
public sealed record SalesforceOAuthError {
    /// <summary>Salesforce's <c>error</c> field, or "unknown_error" when the body was not parseable.</summary>
    public required string Error { get; init; }

    /// <summary>Salesforce's <c>error_description</c>, verbatim.</summary>
    public required string ErrorDescription { get; init; }

    /// <summary>The complete response body, untouched.</summary>
    public required string RawResponse { get; init; }

    /// <summary>Which setup step this most likely points at, or null when the error is not recognised.</summary>
    public string? Guidance { get; init; }

    /// <summary>A one-line summary safe to store in the connection's LastError.</summary>
    public string Summary => Guidance is null
        ? $"{Error}: {ErrorDescription}"
        : $"{Error}: {ErrorDescription} — {Guidance}";
}
