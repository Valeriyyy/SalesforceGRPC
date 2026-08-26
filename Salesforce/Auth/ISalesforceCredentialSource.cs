namespace Salesforce.Auth;

/// <summary>
/// Supplies the current Salesforce credentials, and takes back what happened when they were used.
/// </summary>
/// <remarks>
/// The seam between this project and the stored Org Connection. It exists in both directions on purpose: a
/// token provider that could only read would leave the connection's state and LastError to be inferred by
/// whoever called it, and they would drift.
/// <para>
/// Read through this on every request rather than capturing the result. Credentials are editable while the
/// service runs, and the whole point of moving them out of appsettings.json was that a change should not need
/// a restart.
/// </para>
/// </remarks>
public interface ISalesforceCredentialSource {
    /// <summary>The current credentials, or null when no Org Connection has been set up.</summary>
    Task<SalesforceCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The discovered instance URL, or null before a token has ever succeeded.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetCredentialsAsync"/> because every outgoing REST call needs the host and
    /// none of them need the private key, and decrypting a key to read a URL would put plaintext key
    /// material on the heap once per HTTP request.
    /// </remarks>
    Task<string?> GetOrgUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a token exchange succeeded, along with the org details discovered from the response.
    /// </summary>
    /// <remarks>
    /// An <paramref name="orgId"/> that differs from one already stored must be refused rather than written:
    /// it means the credentials now point at a different org, and letting it through would leave every
    /// Binding writing another org's records into tables mapped for the old one. The implementation raises
    /// that as an error and changes nothing.
    /// </remarks>
    Task RecordTokenSuccessAsync(string orgUrl, string? orgId, CancellationToken cancellationToken = default);

    /// <summary>Records a failed token exchange, keeping the raw Salesforce text.</summary>
    Task RecordTokenFailureAsync(string error, CancellationToken cancellationToken = default);
}
