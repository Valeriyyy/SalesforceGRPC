namespace Salesforce.Auth;

/// <summary>
/// Thrown when a token is requested but no Org Connection has been set up.
/// </summary>
/// <remarks>
/// Distinct from a rejected token: nothing has been tried, and the remedy is setup rather than diagnosis.
/// </remarks>
public sealed class NoOrgConnectionException : InvalidOperationException {
    public NoOrgConnectionException()
        : base("No Salesforce Org Connection has been set up, so no access token can be obtained. " +
               "Configure one through the API.") { }
}
