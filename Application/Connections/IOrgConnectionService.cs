using DTO;

namespace Application.Connections;

/// <summary>
/// Setting up, verifying and tearing down the Org Connection.
/// </summary>
/// <remarks>
/// Owns the whole user-facing lifecycle. Connection State itself turns in
/// <see cref="StoredOrgConnectionSource"/>, because it follows the outcome of a token request and the token
/// provider is what makes those.
/// </remarks>
public interface IOrgConnectionService {
    Task<OrgConnectionDTO> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores connection details and generates a Signing Keypair. Leaves the connection Incomplete.</summary>
    Task<OrgConnectionDTO> SaveAsync(SaveOrgConnectionDTO request, CancellationToken cancellationToken = default);

    /// <summary>Builds the Salesforce authorize URL for the Administering User's browser.</summary>
    Task<BootstrapStartDTO> StartBootstrapAsync(CancellationToken cancellationToken = default);

    /// <summary>Handles the OAuth callback: verifies state, exchanges the code, configures, then verifies.</summary>
    Task<OrgConnectionDTO> CompleteBootstrapAsync(string code, string state, CancellationToken cancellationToken = default);

    /// <summary>Requests a JWT token for both identities, recording the outcome on the connection.</summary>
    Task<OrgConnectionDTO> VerifyAsync(CancellationToken cancellationToken = default);

    /// <summary>What a Disconnect would destroy.</summary>
    Task<DisconnectPreviewDTO> PreviewDisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Wipes the connection and every piece of org-specific state. Nothing in Salesforce is touched.</summary>
    Task<DisconnectPreviewDTO> DisconnectAsync(ConfirmDisconnectDTO confirmation, CancellationToken cancellationToken = default);

    /// <summary>The Signing Certificate as PEM, for a user registering it in Setup by hand.</summary>
    Task<string> GetCertificatePemAsync(CancellationToken cancellationToken = default);
}
