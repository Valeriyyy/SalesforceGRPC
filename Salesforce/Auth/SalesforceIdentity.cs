namespace Salesforce.Auth;

/// <summary>
/// Which Salesforce user an access token is being requested for.
/// </summary>
/// <remarks>
/// The JWT Bearer flow names a user in the <c>sub</c> claim, so one External Client App and one Signing
/// Keypair authenticate both identities — they differ only in that claim. Two of them exist because the
/// event stream should run under permissions the customer chose, while setup-level work needs an
/// administrator, and conflating them would mean granting the stream more than it needs.
/// </remarks>
public enum SalesforceIdentity {
    /// <summary>The user the event stream runs as. Their permissions bound what the worker can see.</summary>
    RunAsUser,

    /// <summary>The administrator who approved setup, under whom Tooling and Metadata work runs.</summary>
    AdministeringUser
}
