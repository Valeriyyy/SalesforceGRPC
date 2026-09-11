using Application.Connections;
using Application.Targets;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace SalesforceGRPCTest;

/// <summary>
/// The hot-swap contract: a repository is built from the stored Target Connection, held until invalidated,
/// and rebuilt from whatever is stored afterwards. Asking when nothing is configured is an error with a name,
/// not a null.
/// </summary>
public class TargetConnectionProviderTests {
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ITargetConnectionRepository _repository = Substitute.For<ITargetConnectionRepository>();
    private readonly ISecretProtector _protector = Substitute.For<ISecretProtector>();
    private readonly ITargetEngineProfile _postgres = Substitute.For<ITargetEngineProfile>();
    private readonly ITargetEngineProfile _sqlite = Substitute.For<ITargetEngineProfile>();
    private readonly IRepository _postgresRepository = Substitute.For<IRepository>();
    private readonly IRepository _sqliteRepository = Substitute.For<IRepository>();

    public TargetConnectionProviderTests() {
        _postgres.Engine.Returns(TargetDatabaseEngine.Postgres);
        _postgres.BuildConnectionString(Arg.Any<TargetConnectionDetails>()).Returns("pg");
        _postgres.CreateRepository("pg").Returns(_postgresRepository);

        _sqlite.Engine.Returns(TargetDatabaseEngine.Sqlite);
        _sqlite.BuildConnectionString(Arg.Any<TargetConnectionDetails>()).Returns("lite");
        _sqlite.CreateRepository("lite").Returns(_sqliteRepository);

        _protector.Unprotect("cipher").Returns("plain");
    }

    private TargetConnectionProvider NewProvider() {
        var catalog = Substitute.For<ITargetEngineCatalog>();
        catalog.For(TargetDatabaseEngine.Postgres).Returns(_postgres);
        catalog.For(TargetDatabaseEngine.Sqlite).Returns(_sqlite);
        return new TargetConnectionProvider(_repository, catalog, _protector, NullLogger<TargetConnectionProvider>.Instance);
    }

    private static TargetConnection Stored(TargetDatabaseEngine engine = TargetDatabaseEngine.Postgres) => new() {
        Id = 1, Engine = engine, Host = "db", Port = 5432, DatabaseName = "wh", Username = "u",
        PasswordEncrypted = "cipher", ConnectionState = ConnectionState.Connected
    };

    [Fact]
    public async Task WithNothingConfigured_AskingForTheRepository_ThrowsANamedException() {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns((TargetConnection?)null);

        await Assert.ThrowsAsync<NoTargetDatabaseException>(() => NewProvider().GetRepositoryAsync(Ct));
    }

    [Fact]
    public async Task TheRepository_IsBuiltFromTheStoredDetails_WithThePasswordDecrypted() {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(Stored());

        var repository = await NewProvider().GetRepositoryAsync(Ct);

        Assert.Same(_postgresRepository, repository);
        _postgres.Received(1).BuildConnectionString(Arg.Is<TargetConnectionDetails>(d =>
            d.Host == "db" && d.DatabaseName == "wh" && d.Username == "u" && d.Password == "plain"));
    }

    [Fact]
    public async Task TheRepository_IsHeld_NotRebuiltPerCall() {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(Stored());
        var provider = NewProvider();

        var first = await provider.GetRepositoryAsync(Ct);
        var second = await provider.GetRepositoryAsync(Ct);

        Assert.Same(first, second);
        await _repository.Received(1).GetAsync(Arg.Any<CancellationToken>());
        _postgres.Received(1).CreateRepository("pg");
    }

    [Fact]
    public async Task AfterInvalidate_TheRepository_IsRebuiltFromWhateverIsStoredNow() {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(Stored(TargetDatabaseEngine.Postgres));
        var provider = NewProvider();
        var before = await provider.GetRepositoryAsync(Ct);

        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(Stored(TargetDatabaseEngine.Sqlite));
        provider.Invalidate();
        var after = await provider.GetRepositoryAsync(Ct);

        Assert.Same(_postgresRepository, before);
        Assert.Same(_sqliteRepository, after);
    }

    /// <summary>
    /// A lost protecting key must not present as "not configured", or the user re-runs setup instead of
    /// restoring the key.
    /// </summary>
    [Fact]
    public async Task AnUndecryptablePassword_IsReportedAsUnreadable_NotAsMissing() {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(Stored());
        _protector.Unprotect("cipher").Returns(_ => throw new SecretsUnreadableException("key ring gone"));

        await Assert.ThrowsAsync<SecretsUnreadableException>(() => NewProvider().GetRepositoryAsync(Ct));
    }
}
