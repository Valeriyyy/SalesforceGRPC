using DTO;

namespace SalesforceGrpc.ViewModels;

/// <summary>Where the Org Connection is in its setup, which decides what the page shows and its one primary action.</summary>
public enum OrgConnectionPhase {
    NotStarted,
    ReadyToApprove,
    NeedsConsumerSecret,
    Approved,
    Unverified,
    Connected,
    Failed
}

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

/// <summary>What the user typed, shown back to them. The Consumer Secret is never part of it.</summary>
public sealed record OrgConnectionDetailsView(string ConsumerKey, string AdministeringUsername, string RunAsUsername, bool IsSandbox);

/// <summary>
/// The org this installation is bound to. The org id outlives an edit of the details; the URL and the last
/// success do not.
/// </summary>
public sealed record OrgView(string OrgId, string? OrgUrl, DateTime? LastConnectedAt);

/// <summary>The Signing Certificate, for comparing against Setup and for Manual Registration.</summary>
public sealed record CertificateView(string Fingerprint, DateTime? ExpiresAt, string DownloadHref);

/// <summary>What the last Bootstrap's Self-Configuration did, and what it left to do by hand.</summary>
public sealed record SelfConfigurationView(DateTime At, bool Configured, string Summary, IReadOnlyList<string> ManualSteps) {
    public static SelfConfigurationView? From(SelfConfigurationDTO? outcome) => outcome is null
        ? null
        : new(outcome.At, outcome.Configured, outcome.Summary, outcome.ManualSteps);
}

/// <summary>Whether stored credentials can be written and read. See <see cref="SecretProtectionDTO"/>.</summary>
public enum SecretProtectionStatus {
    Ready,
    NotConfigured,
    Unreadable
}

/// <summary>
/// When not Ready, the page replaces the form with the guidance: re-running setup would orphan the External
/// Client App.
/// </summary>
public sealed record SecretProtectionView(SecretProtectionStatus Status, string ProtectingKey, string? Guidance) {
    public static SecretProtectionView From(SecretProtectionDTO protection) => new(
        Enum.TryParse<SecretProtectionStatus>(protection.Status, out var status) ? status : SecretProtectionStatus.Unreadable,
        protection.ProtectingKey,
        protection.Guidance);
}

/// <summary>
/// A Salesforce failure, both halves: what it most likely means, and Salesforce's own words.
/// </summary>
/// <remarks>Guidance is null when the error is not recognised; the page says so rather than guessing.</remarks>
public sealed record SalesforceErrorView(string Error, string ErrorDescription, string? Guidance, string RawResponse,
    DateTime? OccurredAt) {

    public static SalesforceErrorView? From(OAuthFailureDTO? failure) => failure is null
        ? null
        : new(failure.Error, failure.ErrorDescription, failure.Guidance, failure.RawResponse, failure.OccurredAt);
}
