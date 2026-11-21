namespace Salesforce.Writeback;

/// <summary>Salesforce refused the Run-as User's session. Retrying will not help until the Org Connection is fixed.</summary>
public sealed class SalesforceUnauthorizedException : Exception {
    public SalesforceUnauthorizedException(string message) : base(message) { }
}
