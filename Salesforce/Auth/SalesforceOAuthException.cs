namespace Salesforce.Auth;

/// <summary>Thrown when Salesforce rejects a token or authorization request.</summary>
public sealed class SalesforceOAuthException : Exception {
    public SalesforceOAuthException(SalesforceOAuthError error) : base(error.Summary) {
        Error = error;
    }

    public SalesforceOAuthError Error { get; }
}
