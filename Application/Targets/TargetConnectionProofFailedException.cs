namespace Application.Targets;

/// <summary>
/// Thrown when a repoint's new database fails its proof. Nothing has been destroyed.
/// </summary>
/// <remarks>
/// Carries the summary and the driver's message separately, as the stored last error does, so the API can
/// return both and the page can show the summary with the searchable text behind it.
/// </remarks>
public sealed class TargetConnectionProofFailedException : InvalidOperationException {
    public TargetConnectionProofFailedException(string message, string rawResponse, Exception? innerException = null)
        : base(message, innerException) {
        RawResponse = rawResponse;
    }

    /// <summary>The driver's message, untouched.</summary>
    public string RawResponse { get; }
}
