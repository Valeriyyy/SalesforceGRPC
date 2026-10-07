using DTO;
using SalesforceGrpc.ViewModels;

namespace SalesforceGRPCTest;

/// <summary>
/// Covers what the Target Connection page shows: which phase the connection is in, and what travels with it.
/// </summary>
public class TargetConnectionViewTests {
    private static readonly SecretProtectionDTO Ready = new() { Status = "Ready", ProtectingKey = "certificate CN=sync" };

    private static readonly IReadOnlyList<EngineDefinitionDTO> Engines = [
        new() {
            Engine = "Postgres", IsAvailable = true, Fields = [
                new() { Name = "host", Label = "Host", Kind = "String", Required = true },
                new() { Name = "port", Label = "Port", Kind = "Int", Required = true, Default = "5432" },
                new() { Name = "password", Label = "Password", Kind = "Secret", Required = true },
                new() { Name = "sslMode", Label = "SSL mode", Kind = "Choice", Default = "Prefer", Choices = ["Disable", "Prefer", "Require"] }
            ]
        },
        new() { Engine = "SqlServer", IsAvailable = false, UnavailableReason = "Not in this build.", Fields = [] }
    ];

    private static readonly RepointPreviewDTO NoBindings = new() { Bindings = 0, FieldMappings = 0 };

    private static TargetConnectionDTO Fresh() => new() { Exists = false, SecretProtection = Ready };

    private static TargetConnectionDTO Stored(string state, TargetConnectionFailureDTO? lastError = null) => new() {
        Exists = true,
        ConnectionState = state,
        Engine = "Postgres",
        Host = "db.internal",
        Port = 5432,
        DatabaseName = "warehouse",
        Username = "loader",
        Options = new Dictionary<string, string> { ["sslMode"] = "Require" },
        HasPassword = true,
        LastConnectedAt = state == "Connected" ? new DateTime(2026, 10, 1, 9, 0, 0) : null,
        LastError = lastError,
        SecretProtection = Ready
    };

    private static readonly TargetConnectionFailureDTO Refused = new() {
        Message = "The Target Database could not be reached.",
        RawResponse = "28P01: password authentication failed for user \"loader\"",
        OccurredAt = new DateTime(2026, 10, 2, 8, 0, 0)
    };

    [Fact]
    public void AFreshInstall_IsNotStarted_WithNoConnection_AndStillListsTheEngines() {
        var view = TargetConnectionView.For(Fresh(), Engines, NoBindings, orgConnectionOk: true);

        Assert.Equal(TargetConnectionPhase.NotStarted, view.Phase);
        Assert.Null(view.Connection);
        Assert.Equal(["Postgres", "SqlServer"], view.Engines.Select(e => e.Engine));
        Assert.False(view.IdentityLocked);
    }

    /// <summary>An engine this build cannot use is listed greyed out with its reason, not left out.</summary>
    [Fact]
    public void TheEngines_CarryTheirFieldsAvailabilityAndReason() {
        var view = TargetConnectionView.For(Fresh(), Engines, NoBindings, orgConnectionOk: true);

        var postgres = view.Engines[0];
        Assert.True(postgres.IsAvailable);
        Assert.Equal(new[] { FieldKindView.String, FieldKindView.Int, FieldKindView.Secret, FieldKindView.Choice },
            postgres.Fields.Select(f => f.Kind));
        Assert.Equal("5432", postgres.Fields[1].Default);
        Assert.Equal(["Disable", "Prefer", "Require"], postgres.Fields[3].Choices!);

        var sqlServer = view.Engines[1];
        Assert.False(sqlServer.IsAvailable);
        Assert.Equal("Not in this build.", sqlServer.UnavailableReason);
    }

    [Fact]
    public void AProvedConnection_IsConnected_AndCarriesItsDetails() {
        var view = TargetConnectionView.For(Stored("Connected"), Engines, NoBindings, orgConnectionOk: true);

        Assert.Equal(TargetConnectionPhase.Connected, view.Phase);
        var connection = view.Connection!;
        Assert.Equal("Postgres", connection.Engine);
        Assert.Equal("db.internal", connection.Host);
        Assert.Equal(5432, connection.Port);
        Assert.Equal("warehouse", connection.DatabaseName);
        Assert.Equal("loader", connection.Username);
        Assert.Equal("Require", connection.Options["sslMode"]);
        Assert.True(connection.HasPassword);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), connection.LastConnectedAt);
        Assert.Null(connection.LastError);
    }

    /// <summary>Stored but never proved: "what you typed did not work", not "something broke".</summary>
    [Fact]
    public void AConnectionThatHasNeverWorked_IsIncomplete_AndCarriesTheLastError() {
        var view = TargetConnectionView.For(Stored("Incomplete", Refused), Engines, NoBindings, orgConnectionOk: true);

        Assert.Equal(TargetConnectionPhase.Incomplete, view.Phase);
        Assert.Equal(new TargetDatabaseErrorView(Refused.Message, Refused.RawResponse, Refused.OccurredAt), view.Connection!.LastError);
    }

    [Fact]
    public void AConnectionThatStoppedWorking_IsFailed_AndCarriesTheLastError() {
        var view = TargetConnectionView.For(Stored("Failed", Refused), Engines, NoBindings, orgConnectionOk: true);

        Assert.Equal(TargetConnectionPhase.Failed, view.Phase);
        Assert.Equal(Refused.RawResponse, view.Connection!.LastError!.RawResponse);
    }

    /// <summary>With nothing to destroy, the identity is editable. See the amendment to docs/adr/0004.</summary>
    [Fact]
    public void WithNoBindings_TheIdentityIsNotLocked() {
        var view = TargetConnectionView.For(Stored("Connected"), Engines, NoBindings, orgConnectionOk: true);

        Assert.False(view.IdentityLocked);
        Assert.Equal(0, view.Bindings);
    }

    [Fact]
    public void WithBindings_TheIdentityIsLocked_AndTheCountsTravelForTheRepointConfirmation() {
        var view = TargetConnectionView.For(Stored("Connected"), Engines,
            new RepointPreviewDTO { Bindings = 3, FieldMappings = 12 }, orgConnectionOk: true);

        Assert.True(view.IdentityLocked);
        Assert.Equal(3, view.Bindings);
        Assert.Equal(12, view.FieldMappings);
    }

    [Fact]
    public void ASecretProtectionProblem_TravelsWithItsGuidance() {
        var connection = Stored("Connected") with {
            SecretProtection = new SecretProtectionDTO { Status = "Unreadable", ProtectingKey = "none", Guidance = "Restore the certificate." }
        };

        var view = TargetConnectionView.For(connection, Engines, NoBindings, orgConnectionOk: true);

        Assert.Equal(new SecretProtectionView(SecretProtectionStatus.Unreadable, "none", "Restore the certificate."), view.SecretProtection);
    }

    /// <summary>The Target Connection can be set up first; the page only notes that nothing flows yet.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhetherTheOrgConnectionIsOk_Travels(bool orgConnectionOk) {
        var view = TargetConnectionView.For(Fresh(), Engines, NoBindings, orgConnectionOk);

        Assert.Equal(orgConnectionOk, view.OrgConnectionOk);
    }
}
