namespace Application.Connections;

/// <summary>
/// Thrown when a token exchange succeeds against an org that is not the one this application is connected to.
/// </summary>
/// <remarks>
/// Carries both org ids because the first question the user will ask is "which one is which", and the answer
/// determines whether they meant to do this.
/// </remarks>
public sealed class OrgMismatchException : InvalidOperationException {
    public OrgMismatchException(string storedOrgId, string discoveredOrgId)
        : base($"These credentials authenticate against org {discoveredOrgId}, but this application is " +
               $"connected to org {storedOrgId}. Nothing has been changed. To move to a different org, use " +
               "Disconnect — it will destroy the Bindings, Field Mappings and cached schemas belonging to " +
               "the current org, which is why it is a deliberate action rather than something a credential " +
               "edit does silently.") {
        StoredOrgId = storedOrgId;
        DiscoveredOrgId = discoveredOrgId;
    }

    public string StoredOrgId { get; }
    public string DiscoveredOrgId { get; }
}
