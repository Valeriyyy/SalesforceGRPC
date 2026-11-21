namespace DTO;

/// <summary>
/// A Salesforce OAuth failure, translated and raw.
/// </summary>
/// <remarks>
/// Both halves, always. The translation table will be incomplete, and an unrecognised error that gets
/// replaced by a friendly guess is worse than no translation at all — the raw text is the only thing that can
/// be searched for or pasted into a support conversation.
/// </remarks>
public record OAuthFailureDTO {
    /// <summary>Salesforce's <c>error</c> field, e.g. "invalid_grant".</summary>
    public string Error { get; set; } = "";

    /// <summary>Salesforce's <c>error_description</c>, verbatim.</summary>
    public string ErrorDescription { get; set; } = "";

    /// <summary>What this most likely means about the setup, or null when the error is not recognised.</summary>
    public string? Guidance { get; set; }

    /// <summary>The complete response body, untouched.</summary>
    public string RawResponse { get; set; } = "";

    public DateTime? OccurredAt { get; set; }
}
