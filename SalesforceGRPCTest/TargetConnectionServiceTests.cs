using Application.Bindings;
using Application.Connections;
using Application.Targets;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.ComponentModel.DataAnnotations;

namespace SalesforceGRPCTest;

/// <summary>
/// The Target Connection lifecycle, driven through <see cref="ITargetConnectionService"/> — the one seam
/// this feature is tested at.
/// </summary>
/// <remarks>
/// The engine profile is substituted so the "proof" is a substituted repository whose schema read either
/// answers or throws. What is asserted is what got recorded, what got signalled, and what the read model
/// carries — never how the service went about it.
/// </remarks>
public class TargetConnectionServiceTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ITargetConnectionRepository _repository = Substitute.For<ITargetConnectionRepository>();
    private readonly ITargetConnectionProvider _provider = Substitute.For<ITargetConnectionProvider>();
    private readonly ISecretProtector _protector = Substitute.For<ISecretProtector>();
    private readonly IConfigurationChangeSignal _signal = Substitute.For<IConfigurationChangeSignal>();
    private readonly ITargetEngineCatalog _engines = Substitute.For<ITargetEngineCatalog>();
    private readonly ITargetEngineProfile _postgres = Substitute.For<ITargetEngineProfile>();
    private readonly IRepository _provedRepository = Substitute.For<IRepository>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));

    public TargetConnectionServiceTests() {
        _protector.IsAvailable.Returns(true);
        _protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        _protector.Unprotect(Arg.Any<string>()).Returns(call => call.Arg<string>()["enc:".Length..]);

        _postgres.Engine.Returns(TargetDatabaseEngine.Postgres);
        _postgres.IsAvailable.Returns(true);
        _postgres.Validate(Arg.Any<TargetConnectionDetails>()).Returns([]);
        _postgres.BuildConnectionString(Arg.Any<TargetConnectionDetails>()).Returns("pg");
        _postgres.CreateRepository("pg").Returns(_provedRepository);
        _engines.For(TargetDatabaseEngine.Postgres).Returns(_postgres);
        _engines.All.Returns([_postgres]);

        // The proof succeeds unless a test says otherwise.
        _provedRepository.GetSchemaMetadata(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        // The repository echoes what it was given, as the real one does through RETURNING.
        _repository.UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                var given = call.Arg<TargetConnection>();
                given.Id = 1;
                given.ConnectionState = ConnectionState.Incomplete;
                return given;
            });
    }

    private TargetConnectionService NewService() =>
        new(_repository, _provider, _engines, _protector, _signal, NullLogger<TargetConnectionService>.Instance, _time);

    private static SaveTargetConnectionDTO PostgresRequest(string host = "db.internal", string password = "s3cret") => new() {
        Engine = "Postgres", Host = host, Port = 5432, DatabaseName = "warehouse", Username = "loader", Password = password
    };

    private void WithStored(TargetConnection? connection) =>
        _provider.GetAsync(Arg.Any<CancellationToken>()).Returns(connection);

    private static TargetConnection Stored(ConnectionState state = ConnectionState.Connected, string host = "db.internal") => new() {
        Id = 1, Engine = TargetDatabaseEngine.Postgres, Host = host, Port = 5432, DatabaseName = "warehouse",
        Username = "loader", PasswordEncrypted = "enc:s3cret", ConnectionState = state
    };

    [Fact]
    public async Task SavingValidDetails_ProvesThem_StoresThemEncrypted_AndRecordsConnected() {
        WithStored(null);

        await NewService().SaveAsync(PostgresRequest(), Ct);

        await _repository.Received(1).UpsertAsync(Arg.Is<TargetConnection>(c =>
            c.Engine == TargetDatabaseEngine.Postgres && c.Host == "db.internal" && c.Username == "loader"
            && c.PasswordEncrypted == "enc:s3cret"), Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordSuccessAsync(_time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        await _provedRepository.Received(1).GetSchemaMetadata(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _provider.Received().Invalidate();
        _signal.Received().Signal();
    }

    /// <summary>
    /// A user whose database is briefly unreachable must still be able to record what they typed.
    /// </summary>
    [Fact]
    public async Task SavingDetailsThatFailTheirProof_StillStoresThem_AsIncompleteWithTheError() {
        WithStored(null);
        _provedRepository.GetSchemaMetadata(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Npgsql.NpgsqlException("28P01: password authentication failed for user \"loader\""));

        await NewService().SaveAsync(PostgresRequest(), Ct);

        await _repository.Received(1).UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).RecordIncompleteAsync(
            Arg.Any<string>(), Arg.Is<string>(raw => raw.Contains("28P01")), _time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordSuccessAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordFailureAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheReadModel_CarriesEveryDetailButThePassword() {
        WithStored(new TargetConnection {
            Id = 1, Engine = TargetDatabaseEngine.Postgres, Host = "db.internal", Port = 5432, DatabaseName = "warehouse",
            Username = "loader", PasswordEncrypted = "enc:s3cret", ConnectionState = ConnectionState.Failed,
            Options = new Dictionary<string, string> { ["sslMode"] = "Require" },
            LastError = "The database refused the credentials.", LastErrorRaw = "28P01", LastErrorAt = new DateTime(2026, 9, 9)
        });

        var dto = await NewService().GetAsync(Ct);

        Assert.True(dto.Exists);
        Assert.Equal("Failed", dto.ConnectionState);
        Assert.Equal("db.internal", dto.Host);
        Assert.Equal("loader", dto.Username);
        Assert.Equal("Require", dto.Options["sslMode"]);
        Assert.True(dto.HasPassword);
        Assert.Equal("28P01", dto.LastError!.RawResponse);
        Assert.DoesNotContain("s3cret", System.Text.Json.JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task Retest_MovesAnIncompleteConnectionToConnected_WhenTheProofSucceeds() {
        WithStored(Stored(ConnectionState.Incomplete));

        await NewService().RetestAsync(Ct);

        await _repository.Received(1).RecordSuccessAsync(_time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        _postgres.Received().BuildConnectionString(Arg.Is<TargetConnectionDetails>(d => d.Password == "s3cret"));
        _signal.Received().Signal();
    }

    [Fact]
    public async Task Retest_MovesAConnectedConnectionToFailed_WhenTheProofFails() {
        WithStored(Stored(ConnectionState.Connected));
        _provedRepository.GetSchemaMetadata(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Npgsql.NpgsqlException("timeout"));

        await NewService().RetestAsync(Ct);

        await _repository.Received(1).RecordFailureAsync(Arg.Any<string>(), "timeout", _time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordIncompleteAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retest_LeavesAnIncompleteConnectionIncomplete_WhenTheProofFails() {
        WithStored(Stored(ConnectionState.Incomplete));
        _provedRepository.GetSchemaMetadata(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Npgsql.NpgsqlException("timeout"));

        await NewService().RetestAsync(Ct);

        await _repository.Received(1).RecordIncompleteAsync(Arg.Any<string>(), "timeout", _time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordFailureAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The worker is where the connection is genuinely exercised, so its failures write state.</summary>
    [Fact]
    public async Task AWriteFailureReportedByTheWorker_MovesAConnectedConnectionToFailed() {
        WithStored(Stored(ConnectionState.Connected));

        await NewService().RecordWriteFailureAsync(new Npgsql.NpgsqlException("connection refused"), Ct);

        await _repository.Received(1).RecordFailureAsync(Arg.Any<string>(), "connection refused", _time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        _provider.Received().Invalidate();
    }

    [Fact]
    public async Task Retest_RecoversAFailedConnection_WhenTheDatabaseIsBack() {
        WithStored(Stored(ConnectionState.Failed));

        var dto = await NewService().RetestAsync(Ct);

        await _repository.Received(1).RecordSuccessAsync(_time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retest_WithNothingConfigured_ThrowsANamedException() {
        WithStored(null);

        await Assert.ThrowsAsync<NoTargetDatabaseException>(() => NewService().RetestAsync(Ct));
    }

    [Fact]
    public async Task SavingAnUnavailableEngine_IsRefusedWithItsReason_BeforeAnythingIsStored() {
        WithStored(null);
        var sqlServer = Substitute.For<ITargetEngineProfile>();
        sqlServer.Engine.Returns(TargetDatabaseEngine.SqlServer);
        sqlServer.IsAvailable.Returns(false);
        sqlServer.UnavailableReason.Returns("The SQL Server driver is not implemented yet.");
        _engines.For(TargetDatabaseEngine.SqlServer).Returns(sqlServer);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService().SaveAsync(PostgresRequest() with { Engine = "SqlServer" }, Ct));

        Assert.Contains("not implemented", ex.Message);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavingInvalidDetails_IsRefusedWithTheProfilesErrors_BeforeAnythingIsStored() {
        WithStored(null);
        _postgres.Validate(Arg.Any<TargetConnectionDetails>()).Returns(["Host is required."]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().SaveAsync(PostgresRequest(), Ct));

        Assert.Contains("Host is required.", ex.Message);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavingAnUnknownEngineName_IsRefused() {
        WithStored(null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            NewService().SaveAsync(PostgresRequest() with { Engine = "Oracle" }, Ct));
    }

    /// <summary>
    /// Refused rather than stored in the clear. An application that protects nothing while claiming to is
    /// worse than one that says it cannot yet.
    /// </summary>
    [Fact]
    public async Task WithoutAProtectingKey_APasswordIsNotStored() {
        WithStored(null);
        _protector.IsAvailable.Returns(false);

        await Assert.ThrowsAsync<ValidationException>(() => NewService().SaveAsync(PostgresRequest(), Ct));

        await _repository.DidNotReceive().UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditingOnlyTheCredentials_Applies_AndDestroysNothing() {
        WithStored(Stored());

        await NewService().SaveAsync(PostgresRequest(password: "rotated"), Ct);

        await _repository.Received(1).UpsertAsync(Arg.Is<TargetConnection>(c => c.PasswordEncrypted == "enc:rotated"), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RepointAsync(Arg.Any<TargetConnection>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("host", "other-db.internal")]
    [InlineData("database", "other-warehouse")]
    public async Task ChangingTheIdentityThroughSave_IsRefused_AndNothingIsStored(string which, string value) {
        WithStored(Stored());
        var request = which == "host"
            ? PostgresRequest(host: value)
            : PostgresRequest() with { DatabaseName = value };

        await Assert.ThrowsAsync<TargetConnectionIdentityChangedException>(() => NewService().SaveAsync(request, Ct));

        await _repository.DidNotReceive().UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangingTheEngineThroughSave_IsRefused() {
        WithStored(Stored());
        var sqlite = Substitute.For<ITargetEngineProfile>();
        sqlite.Engine.Returns(TargetDatabaseEngine.Sqlite);
        sqlite.IsAvailable.Returns(true);
        sqlite.Validate(Arg.Any<TargetConnectionDetails>()).Returns([]);
        _engines.For(TargetDatabaseEngine.Sqlite).Returns(sqlite);

        await Assert.ThrowsAsync<TargetConnectionIdentityChangedException>(() =>
            NewService().SaveAsync(new SaveTargetConnectionDTO { Engine = "Sqlite", FilePath = "/x.db" }, Ct));
    }

    [Fact]
    public async Task EditingOnlyTheOptions_Applies_AndDestroysNothing() {
        WithStored(Stored());
        var request = PostgresRequest();
        request.Options["sslMode"] = "Require";

        await NewService().SaveAsync(request, Ct);

        await _repository.Received(1).UpsertAsync(Arg.Is<TargetConnection>(c => c.Options["sslMode"] == "Require"), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RepointAsync(Arg.Any<TargetConnection>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangingTheFilePathThroughSave_IsRefused() {
        var sqlite = Substitute.For<ITargetEngineProfile>();
        sqlite.Engine.Returns(TargetDatabaseEngine.Sqlite);
        sqlite.IsAvailable.Returns(true);
        sqlite.Validate(Arg.Any<TargetConnectionDetails>()).Returns([]);
        _engines.For(TargetDatabaseEngine.Sqlite).Returns(sqlite);
        WithStored(new TargetConnection { Id = 1, Engine = TargetDatabaseEngine.Sqlite, FilePath = "/a.db", ConnectionState = ConnectionState.Connected });

        await Assert.ThrowsAsync<TargetConnectionIdentityChangedException>(() =>
            NewService().SaveAsync(new SaveTargetConnectionDTO { Engine = "Sqlite", FilePath = "/b.db" }, Ct));
    }

    /// <summary>A host differing only in case is the same host. DNS is case-insensitive.</summary>
    [Fact]
    public async Task AHostDifferingOnlyInCase_IsNotAnIdentityChange() {
        WithStored(Stored(host: "DB.internal"));

        await NewService().SaveAsync(PostgresRequest(host: "db.internal"), Ct);

        await _repository.Received(1).UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheEngineDefinitions_ListEveryEngine_WithItsFieldsAndAvailability() {
        _postgres.Fields.Returns([
            new FieldDefinition { Name = "host", Label = "Host", Kind = FieldKind.String, Required = true },
            new FieldDefinition { Name = "sslMode", Label = "SSL mode", Kind = FieldKind.Choice, Default = "Prefer", Choices = ["Disable", "Prefer", "Require"] }
        ]);
        var mysql = Substitute.For<ITargetEngineProfile>();
        mysql.Engine.Returns(TargetDatabaseEngine.MySql);
        mysql.IsAvailable.Returns(false);
        mysql.UnavailableReason.Returns("The MySQL driver is not implemented yet.");
        mysql.Fields.Returns([]);
        _engines.All.Returns([_postgres, mysql]);

        var engines = NewService().GetEngines();

        var postgres = Assert.Single(engines, e => e.Engine == "Postgres");
        Assert.True(postgres.IsAvailable);
        var ssl = Assert.Single(postgres.Fields, f => f.Name == "sslMode");
        Assert.Equal("Choice", ssl.Kind);
        Assert.Equal(["Disable", "Prefer", "Require"], ssl.Choices);

        var unavailable = Assert.Single(engines, e => e.Engine == "MySql");
        Assert.False(unavailable.IsAvailable);
        Assert.Equal("The MySQL driver is not implemented yet.", unavailable.UnavailableReason);
    }

    #region Repoint

    private void WithBindings(int bindings, int fieldMappings) =>
        _repository.CountBindingsAsync(Arg.Any<CancellationToken>())
            .Returns(new BindingCounts { Bindings = bindings, FieldMappings = fieldMappings });

    private static RepointTargetConnectionDTO RepointRequest(int expectedBindings, int expectedFieldMappings) => new() {
        Engine = "Postgres", Host = "other-db.internal", Port = 5432, DatabaseName = "warehouse", Username = "loader",
        Password = "s3cret", ExpectedBindings = expectedBindings, ExpectedFieldMappings = expectedFieldMappings
    };

    [Fact]
    public async Task TheRepointPreview_ReportsWhatWouldBeDestroyed() {
        WithStored(Stored());
        WithBindings(3, 12);

        var preview = await NewService().PreviewRepointAsync(Ct);

        Assert.Equal(3, preview.Bindings);
        Assert.Equal(12, preview.FieldMappings);
    }

    [Fact]
    public async Task RepointingWithAMatchingConfirmation_DestroysBindings_StoresTheNewTarget_AndRecordsConnected() {
        WithStored(Stored());
        WithBindings(3, 12);
        _repository.RepointAsync(Arg.Any<TargetConnection>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => { var c = call.Arg<TargetConnection>(); c.Id = 1; return (c, new BindingCounts { Bindings = 3, FieldMappings = 12 }); });

        await NewService().RepointAsync(RepointRequest(3, 12), Ct);

        await _repository.Received(1).RepointAsync(Arg.Is<TargetConnection>(c => c.Host == "other-db.internal"),
            _time.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>());
        _provider.Received().Invalidate();
        _signal.Received().Signal();
    }

    /// <summary>A Binding created since the preview aborts the repoint rather than vanishing.</summary>
    [Fact]
    public async Task RepointingWithAStaleConfirmation_DestroysNothing() {
        WithStored(Stored());
        WithBindings(4, 12);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().RepointAsync(RepointRequest(3, 12), Ct));

        Assert.Contains("Nothing has been destroyed", ex.Message);
        await _repository.DidNotReceive().RepointAsync(Arg.Any<TargetConnection>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<TargetConnection>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Destroying Bindings and then discovering the new database is unreachable would leave the user with
    /// neither.
    /// </summary>
    [Fact]
    public async Task RepointingToADatabaseThatFailsItsProof_DestroysNothing() {
        WithStored(Stored());
        WithBindings(3, 12);
        _provedRepository.GetSchemaMetadata(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Npgsql.NpgsqlException("no route to host"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().RepointAsync(RepointRequest(3, 12), Ct));

        Assert.Contains("no route to host", ex.Message);
        await _repository.DidNotReceive().RepointAsync(Arg.Any<TargetConnection>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().RecordFailureAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Destroying every Binding to arrive at the same database would be destruction for nothing.</summary>
    [Fact]
    public async Task RepointingToTheSameDatabase_IsRefused_AndDestroysNothing() {
        WithStored(Stored());
        WithBindings(3, 12);
        var sameIdentity = RepointRequest(3, 12) with { Host = "db.internal" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => NewService().RepointAsync(sameIdentity, Ct));

        Assert.Contains("same database", ex.Message);
        await _repository.DidNotReceive().RepointAsync(Arg.Any<TargetConnection>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepointingWithNothingConfigured_ThrowsANamedException() {
        WithStored(null);

        await Assert.ThrowsAsync<NoTargetDatabaseException>(() => NewService().RepointAsync(RepointRequest(0, 0), Ct));
    }

    #endregion

    [Fact]
    public async Task TheReadModel_ReportsSecretProtectionAsReady_WhenAKeyIsPresent() {
        WithStored(null);
        _protector.ProtectingKeyDescription.Returns("environment variable SALESFORCEGRPC_PROTECTING_CERT");

        var dto = await NewService().GetAsync(Ct);

        Assert.Equal("Ready", dto.SecretProtection.Status);
        Assert.Contains("SALESFORCEGRPC_PROTECTING_CERT", dto.SecretProtection.ProtectingKey);
    }

    /// <summary>
    /// A lost protecting key must not present as "not configured", or the user re-enters the password when
    /// what they need to do is restore the key.
    /// </summary>
    [Fact]
    public async Task TheReadModel_ReportsSecretProtectionAsUnreadable_WhenTheStoredPasswordWillNotDecrypt() {
        WithStored(Stored());
        _protector.TryUnprotect("enc:s3cret", out Arg.Any<string>()).Returns(false);

        var dto = await NewService().GetAsync(Ct);

        Assert.Equal("Unreadable", dto.SecretProtection.Status);
    }

    [Fact]
    public async Task OnAFreshInstall_TheReadModel_SaysSoWithoutThrowing() {
        WithStored(null);

        var dto = await NewService().GetAsync(Ct);

        Assert.False(dto.Exists);
    }
}
