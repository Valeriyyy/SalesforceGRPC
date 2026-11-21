namespace Salesforce.Writeback;

/// <summary>
/// A Salesforce call that failed for a reason that may pass (network, outage, API limits, a locked row). The
/// call decided nothing and may be made again.
/// </summary>
public sealed class SalesforceTransientException : Exception {
    public SalesforceTransientException(string message, Exception? inner = null) : base(message, inner) { }
}
