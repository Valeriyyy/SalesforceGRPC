namespace Salesforce.Clients;

/// <summary>
/// Salesforce refused a whole call for a reason that will not pass on its own (a malformed request, an object
/// or field the Run-as User cannot see). No record in it was written.
/// </summary>
public sealed class SalesforceRequestRefusedException : Exception {
    public int StatusCode { get; }
    public IReadOnlyList<string> StatusCodes { get; }

    public SalesforceRequestRefusedException(int statusCode, IReadOnlyList<string> statusCodes, string body)
        : base($"Salesforce refused the call ({statusCode} {string.Join(", ", statusCodes)}): {body}") {
        StatusCode = statusCode;
        StatusCodes = statusCodes;
    }
}
