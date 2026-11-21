namespace Database.Models;

/// <summary>
/// Where an Org Connection stands with Salesforce.
/// </summary>
/// <remarks>
/// Three states rather than a pair of booleans, so "is set up" and "actually works" can never disagree. Incomplete is not an error: a connection holds a real Signing Keypair from the moment the user
/// supplies their details, and the keypair has to exist before Salesforce can be told about it.
/// </remarks>
public enum ConnectionState {
    /// <summary>Details and a Signing Keypair are stored, but no token has ever succeeded.</summary>
    Incomplete,

    /// <summary>A JWT token succeeded, and OrgUrl and OrgId are known.</summary>
    Connected,

    /// <summary>A token attempt failed. LastError says how.</summary>
    Failed
}
