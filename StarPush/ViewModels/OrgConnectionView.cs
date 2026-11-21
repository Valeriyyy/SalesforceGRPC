using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// The Org Connection page. The server decides the phase and what travels with it; the component only renders.
/// </summary>
public sealed record OrgConnectionView(
    OrgConnectionPhase Phase,
    OrgConnectionDetailsView? Details,
    bool HasConsumerSecret,
    OrgView? Org,
    string CallbackUrl,
    CertificateView? Certificate,
    SalesforceErrorView? LastError,
    SelfConfigurationView? SelfConfiguration,
    SecretProtectionView SecretProtection,
    OrgConnectionNotice? Notice) {

    public const string CertificateHref = "/api/orgconnection/certificate";

    public static OrgConnectionView For(OrgConnectionDTO connection, OrgConnectionNotice? notice) {
        if (!connection.Exists) {
            return new(OrgConnectionPhase.NotStarted, null, false, null, connection.CallbackUrl, null, null, null,
                SecretProtectionView.From(connection.SecretProtection), notice);
        }

        var phase = PhaseOf(connection);

        return new(
            phase,
            new OrgConnectionDetailsView(connection.ConsumerKey, connection.AdministeringUsername,
                connection.RunAsUsername, connection.IsSandbox),
            connection.HasConsumerSecret,
            connection.OrgId is null ? null : new OrgView(connection.OrgId, connection.OrgUrl, connection.LastConnectedAt),
            connection.CallbackUrl,
            new CertificateView(connection.CertificateFingerprint, connection.CertificateExpiresAt, CertificateHref),
            SalesforceErrorView.From(connection.LastError),
            // What was left to do by hand only matters while it still might not be done.
            phase is OrgConnectionPhase.Approved or OrgConnectionPhase.Failed
                ? SelfConfigurationView.From(connection.SelfConfiguration)
                : null,
            SecretProtectionView.From(connection.SecretProtection),
            notice);
    }

    /// <remarks>
    /// Order matters. A rejected token moves the connection to Failed even before it has ever worked, so a held
    /// Bootstrap refresh token (the user approved; nothing has succeeded since) wins over Failed. A known org id
    /// with no Consumer Secret means the details of a connection that has worked were edited: its certificate
    /// is registered already, so it needs Verify rather than the secret.
    /// </remarks>
    private static OrgConnectionPhase PhaseOf(OrgConnectionDTO connection) => connection switch {
        { ConnectionState: "Connected" } => OrgConnectionPhase.Connected,
        { IsApproved: true } => OrgConnectionPhase.Approved,
        { ConnectionState: "Failed" } => OrgConnectionPhase.Failed,
        { HasConsumerSecret: true } => OrgConnectionPhase.ReadyToApprove,
        { OrgId: not null } => OrgConnectionPhase.Unverified,
        _ => OrgConnectionPhase.NeedsConsumerSecret
    };
}
