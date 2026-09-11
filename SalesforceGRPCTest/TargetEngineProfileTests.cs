using Database.Models;
using Database.Targets;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace SalesforceGRPCTest;

/// <summary>
/// The engine profiles' connection-string builders and field validation.
/// </summary>
/// <remarks>
/// This is its own seam because the assembled string is deliberately unobservable from above: built, handed
/// to the connection, dropped, never logged and never returned by the API. The driver's own parser is the
/// source of truth for what the string means — a string the driver reads back the way it was intended is a
/// correct string, whatever it looks like.
/// </remarks>
public class TargetEngineProfileTests {

    /// <summary>
    /// The password that motivates using driver builders at all. Concatenated naively it would end the
    /// password early and flip a security setting.
    /// </summary>
    private const string HostilePassword = "p@ss;Trust Server Certificate=true;x=\"y\"";

    private static PostgresEngineProfile Postgres() => new(NullLoggerFactory.Instance);

    private static TargetConnectionDetails PostgresDetails(string password = HostilePassword,
        IReadOnlyDictionary<string, string>? options = null) => new() {
        Engine = TargetDatabaseEngine.Postgres,
        Host = "db.example.internal",
        Port = 5433,
        DatabaseName = "warehouse",
        Username = "loader",
        Password = password,
        Options = options ?? new Dictionary<string, string>()
    };

    [Fact]
    public void Postgres_APasswordFullOfDelimiters_RoundTripsWithoutChangingAnyOtherSetting() {
        var connectionString = Postgres().BuildConnectionString(PostgresDetails());

        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal(HostilePassword, parsed.Password);
        Assert.Equal("db.example.internal", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("warehouse", parsed.Database);
        Assert.Equal("loader", parsed.Username);
        Assert.False(parsed.TrustServerCertificate);
    }

    [Fact]
    public void Postgres_SslModeOption_LandsOnTheDriverSetting() {
        var details = PostgresDetails(options: new Dictionary<string, string> { ["sslMode"] = "Require" });

        var parsed = new NpgsqlConnectionStringBuilder(Postgres().BuildConnectionString(details));

        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void AnUnrecognisedOption_IsRejectedRatherThanSilentlyDropped() {
        var details = PostgresDetails(options: new Dictionary<string, string> { ["trustServerCertificate"] = "true" });

        var errors = Postgres().Validate(details);

        var error = Assert.Single(errors);
        Assert.Contains("trustServerCertificate", error);
    }

    [Fact]
    public void AMissingRequiredField_IsReportedByName() {
        var details = PostgresDetails() with { Host = "" };

        var errors = Postgres().Validate(details);

        var error = Assert.Single(errors);
        Assert.Contains("Host", error);
    }

    [Fact]
    public void AChoiceOutsideItsAllowedValues_IsRejected() {
        var details = PostgresDetails(options: new Dictionary<string, string> { ["sslMode"] = "Definitely" });

        var errors = Postgres().Validate(details);

        var error = Assert.Single(errors);
        Assert.Contains("Definitely", error);
    }

    [Fact]
    public void ATypedFieldSuppliedAsAnOption_IsRejected() {
        var details = PostgresDetails(options: new Dictionary<string, string> { ["port"] = "5432" });

        var errors = Postgres().Validate(details);

        var error = Assert.Single(errors);
        Assert.Contains("port", error);
    }

    [Fact]
    public void ValidDetails_ProduceNoErrors() {
        Assert.Empty(Postgres().Validate(PostgresDetails()));
    }

    [Fact]
    public void Sqlite_AsksOnlyForAFilePath_AndBuildsFromIt() {
        var profile = new SqliteEngineProfile(NullLoggerFactory.Instance);
        var details = new TargetConnectionDetails { Engine = TargetDatabaseEngine.Sqlite, FilePath = "/var/data/it's;here.db" };

        Assert.Empty(profile.Validate(details));
        var parsed = new SqliteConnectionStringBuilder(profile.BuildConnectionString(details));
        Assert.Equal("/var/data/it's;here.db", parsed.DataSource);
        Assert.Equal([FieldNames.FilePath], profile.Fields.Select(f => f.Name));
    }

    [Theory]
    [InlineData(TargetDatabaseEngine.SqlServer)]
    [InlineData(TargetDatabaseEngine.MySql)]
    public void EnginesWithoutADriver_AreUnavailableWithAReason(TargetDatabaseEngine engine) {
        var profile = new TargetEngineCatalog(AllProfiles()).For(engine);

        Assert.False(profile.IsAvailable);
        Assert.Contains("not implemented", profile.UnavailableReason);
    }

    [Fact]
    public void TheCatalog_KnowsEveryEngine() {
        var catalog = new TargetEngineCatalog(AllProfiles());

        foreach (var engine in Enum.GetValues<TargetDatabaseEngine>()) {
            Assert.Equal(engine, catalog.For(engine).Engine);
        }
    }

    private static IEnumerable<ITargetEngineProfile> AllProfiles() => [
        new PostgresEngineProfile(NullLoggerFactory.Instance),
        new SqlServerEngineProfile(NullLoggerFactory.Instance),
        new MySqlEngineProfile(NullLoggerFactory.Instance),
        new SqliteEngineProfile(NullLoggerFactory.Instance)
    ];
}
