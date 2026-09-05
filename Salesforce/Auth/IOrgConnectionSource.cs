namespace Salesforce.Auth;

/// <summary>
/// Supplies the current Org Connection, and takes back what happened when it was used.
/// </summary>
/// <remarks>
/// The seam between this project and the App Database. It exists in both directions on purpose: a token
/// provider that could only read would leave the connection's state and last error to be inferred by whoever
/// called it, and they would drift.
/// <para>
/// Read through this on every request rather than capturing the result. An Org Connection is editable while
/// the service runs, and the whole point of moving it out of appsettings.json was that a change should not
/// need a restart.
/// </para>
/// </remarks>
public interface IOrgConnectionSource {
    /// <summary>The current Org Connection, or null when none has been set up.</summary>
    Task<OrgConnectionDetails?> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The discovered instance URL, or null before a token has ever succeeded.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetConnectionAsync"/> because every outgoing REST call needs the host and none
    /// of them need the private key, and decrypting a key to read a URL would put plaintext key material on
    /// the heap once per HTTP request.
    /// </remarks>
    Task<string?> GetOrgUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a token exchange succeeded, along with the org details discovered from the response.
    /// </summary>
    /// <remarks>
    /// An <paramref name="orgId"/> that differs from one already stored must be refused rather than written:
    /// it means the connection now points at a different org, and letting it through would leave every
    /// Binding writing another org's records into tables mapped for the old one. The implementation raises
    /// that as an error and changes nothing.
    /// </remarks>
    Task RecordTokenSuccessAsync(string orgUrl, string? orgId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a rejected token exchange, keeping Salesforce's raw response alongside the translation.
    /// </summary>
    /// <remarks>
    /// The whole error, not a summary of it. The translation table is incomplete by construction, so the
    /// untouched text is the only thing a user can search for or paste into a support conversation — and it
    /// has to survive as far as the API, which is the only place they will see it.
    /// </remarks>
    Task RecordTokenFailureAsync(SalesforceOAuthError error, CancellationToken cancellationToken = default);
}
