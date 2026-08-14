using Salesforce.Dtos;
using System.Net;

namespace Salesforce.Clients;

/// <summary>
/// Thrown when a Salesforce Tooling API call fails. Carries the HTTP status and the parsed
/// <c>errors</c> array Salesforce returns, so callers can distinguish (for example) a duplicate
/// developer name from a missing permission instead of only seeing a bare HTTP failure.
/// </summary>
public sealed class SalesforceToolingException : Exception {
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<ToolingError> Errors { get; }
    public string? RawBody { get; }

    public SalesforceToolingException(HttpStatusCode statusCode, IReadOnlyList<ToolingError> errors, string? rawBody = null)
        : base(BuildMessage(statusCode, errors, rawBody)) {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    /// <summary>
    /// The first Salesforce error code, if any. Useful for callers that branch on specific codes
    /// such as DUPLICATE_DEVELOPER_NAME or INSUFFICIENT_ACCESS.
    /// </summary>
    public string? ErrorCode => Errors.Count > 0 ? Errors[0].ErrorCode : null;

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<ToolingError> errors, string? rawBody) {
        if (errors.Count > 0) {
            var detail = string.Join("; ", errors.Select(e => $"{e.ErrorCode}: {e.Message}"));
            return $"Salesforce Tooling API returned {(int)statusCode} {statusCode}. {detail}";
        }
        return string.IsNullOrWhiteSpace(rawBody)
            ? $"Salesforce Tooling API returned {(int)statusCode} {statusCode}."
            : $"Salesforce Tooling API returned {(int)statusCode} {statusCode}. {rawBody}";
    }
}
