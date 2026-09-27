using DTO;
using SalesforceGrpc.ViewModels;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers what the Org Connection page shows: which phase the connection is in, and what travels with it.
/// </summary>
public class OrgConnectionViewTests {
    private const string CallbackUrl = "https://sync.example.com/api/orgconnection/bootstrap/callback";

    private static readonly SecretProtectionDTO Ready = new() { Status = "Ready", ProtectingKey = "certificate CN=sync" };

    private static OrgConnectionDTO Fresh() => new() {
        Exists = false,
        CallbackUrl = CallbackUrl,
        SecretProtection = Ready
    };

    private static OrgConnectionDTO Saved(string state = "Incomplete", bool hasConsumerSecret = false,
        bool isApproved = false, string? orgId = null) => new() {
        Exists = true,
        ConnectionState = state,
        ConsumerKey = "3MVG9key",
        AdministeringUsername = "admin@example.com",
        RunAsUsername = "integration@example.com",
        OrgId = orgId,
        OrgUrl = orgId is null ? null : "https://example.my.salesforce.com",
        CertificateFingerprint = "AA:BB:CC",
        CertificateExpiresAt = new DateTime(2031, 1, 1),
        CallbackUrl = CallbackUrl,
        HasConsumerSecret = hasConsumerSecret,
        IsApproved = isApproved,
        SecretProtection = Ready
    };

    [Fact]
    public void AFreshInstall_IsNotStarted_AndStillShowsTheCallbackUrl() {
        var view = OrgConnectionView.For(Fresh(), notice: null);

        Assert.Equal(OrgConnectionPhase.NotStarted, view.Phase);
        Assert.Equal(CallbackUrl, view.CallbackUrl);
        Assert.Null(view.Details);
        Assert.Null(view.Certificate);
    }

    [Fact]
    public void SavedDetailsWithAConsumerSecret_AreReadyToApprove() {
        var view = OrgConnectionView.For(Saved(hasConsumerSecret: true), notice: null);

        Assert.Equal(OrgConnectionPhase.ReadyToApprove, view.Phase);
        Assert.Equal(new OrgConnectionDetailsView("3MVG9key", "admin@example.com", "integration@example.com", false), view.Details);
        Assert.Equal(new CertificateView("AA:BB:CC", new DateTime(2031, 1, 1), "/api/orgconnection/certificate"), view.Certificate);
    }

    /// <summary>
    /// The secret is only asked for again, not the whole form: everything else is still stored.
    /// </summary>
    [Fact]
    public void SavedDetailsThatHaveNeverWorkedAndLostTheirConsumerSecret_NeedTheConsumerSecret() {
        var view = OrgConnectionView.For(Saved(hasConsumerSecret: false), notice: null);

        Assert.Equal(OrgConnectionPhase.NeedsConsumerSecret, view.Phase);
    }

    private static readonly OAuthFailureDTO NotApproved = new() {
        Error = "invalid_grant",
        ErrorDescription = "user hasn't approved this consumer",
        Guidance = "The Run-as User is not pre-authorized for the External Client App.",
        RawResponse = """{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}""",
        OccurredAt = new DateTime(2026, 9, 26, 10, 0, 0)
    };

    /// <summary>
    /// A rejected token moves the connection to Failed even though it has never worked, so the held Bootstrap
    /// refresh token is what says the user approved and Salesforce has not caught up yet.
    /// </summary>
    [Fact]
    public void AnApprovedConnectionWhoseTokenIsStillRefused_IsApprovedNotFailed_AndCarriesTheRefusal() {
        var connection = Saved("Failed", hasConsumerSecret: true, isApproved: true) with { LastError = NotApproved };

        var view = OrgConnectionView.For(connection, notice: null);

        Assert.Equal(OrgConnectionPhase.Approved, view.Phase);
        Assert.Equal(new SalesforceErrorView(
            "invalid_grant",
            "user hasn't approved this consumer",
            "The Run-as User is not pre-authorized for the External Client App.",
            """{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}""",
            new DateTime(2026, 9, 26, 10, 0, 0)), view.LastError);
    }

    private const string OrgId = "00D000000000001AAA";

    [Fact]
    public void AConnectedConnection_IsConnected_AndSaysWhichOrgAndWhenItLastWorked() {
        var connection = Saved("Connected", orgId: OrgId) with { LastConnectedAt = new DateTime(2026, 9, 26, 9, 0, 0) };

        var view = OrgConnectionView.For(connection, notice: null);

        Assert.Equal(OrgConnectionPhase.Connected, view.Phase);
        Assert.Equal(new OrgView(OrgId, "https://example.my.salesforce.com", new DateTime(2026, 9, 26, 9, 0, 0)), view.Org);
    }

    [Fact]
    public void AConnectionThatStoppedWorking_HasFailed() {
        var connection = Saved("Failed", orgId: OrgId) with { LastError = NotApproved };

        var view = OrgConnectionView.For(connection, notice: null);

        Assert.Equal(OrgConnectionPhase.Failed, view.Phase);
        Assert.NotNull(view.LastError);
    }

    /// <summary>
    /// Editing a connection that has worked resets it to Incomplete without a Consumer Secret, but its
    /// certificate is already registered. Asking for the secret there would send the user through an approval
    /// they do not need; Verify is all it takes.
    /// </summary>
    [Fact]
    public void EditedDetailsOfAConnectionThatHasWorked_AreUnverified_NotMissingTheirConsumerSecret() {
        var view = OrgConnectionView.For(Saved("Incomplete", hasConsumerSecret: false, orgId: OrgId), notice: null);

        Assert.Equal(OrgConnectionPhase.Unverified, view.Phase);
    }

    /// <summary>
    /// The phase still says where the connection stands; the page hides the form and Connect because of the
    /// Secret Protection status, not because of a phase of its own.
    /// </summary>
    [Fact]
    public void UnreadableSecrets_TravelWithTheirGuidance_AndLeaveThePhaseAlone() {
        var connection = Saved("Connected", orgId: OrgId) with {
            SecretProtection = new SecretProtectionDTO {
                Status = "Unreadable",
                ProtectingKey = "certificate CN=sync",
                Guidance = "Restore the original protecting certificate and key ring."
            }
        };

        var view = OrgConnectionView.For(connection, notice: null);

        Assert.Equal(OrgConnectionPhase.Connected, view.Phase);
        Assert.Equal(new SecretProtectionView(SecretProtectionStatus.Unreadable, "certificate CN=sync",
            "Restore the original protecting certificate and key ring."), view.SecretProtection);
    }

    [Fact]
    public void AOneTimeNotice_IsShownOnThePage() {
        var notice = OrgConnectionNotice.Failure("Salesforce did not approve the connection: access_denied");

        var view = OrgConnectionView.For(Saved(hasConsumerSecret: true), notice);

        Assert.Equal(notice, view.Notice);
    }

    private static readonly SelfConfigurationDTO NothingConfigured = new() {
        At = new DateTime(2026, 9, 26, 9, 55, 0),
        Configured = false,
        Summary = "Salesforce was not configured automatically.",
        ManualSteps = ["Upload the Signing Certificate.", "Assign the permission set."]
    };

    /// <summary>What is still missing in Setup is the whole point of the Approved phase.</summary>
    [Fact]
    public void AnApprovedConnection_ShowsWhatSelfConfigurationLeftToDoByHand() {
        var connection = Saved("Failed", hasConsumerSecret: true, isApproved: true) with {
            SelfConfiguration = NothingConfigured
        };

        var outcome = OrgConnectionView.For(connection, notice: null).SelfConfiguration;

        Assert.NotNull(outcome);
        Assert.False(outcome.Configured);
        Assert.Equal("Salesforce was not configured automatically.", outcome.Summary);
        Assert.Equal(["Upload the Signing Certificate.", "Assign the permission set."], outcome.ManualSteps);
    }

    /// <summary>Once it works, the steps it once needed are history rather than instructions.</summary>
    [Fact]
    public void AConnectedConnection_NoLongerShowsTheSelfConfigurationOutcome() {
        var connection = Saved("Connected", orgId: OrgId) with { SelfConfiguration = NothingConfigured };

        Assert.Null(OrgConnectionView.For(connection, notice: null).SelfConfiguration);
    }
}
