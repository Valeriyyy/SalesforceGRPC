using Database.Models;
using Database.Targets;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
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
    /// password early and redirect the connection to another host.
    /// </summary>
    private const string HostilePassword = "p@ss;Host=evil.example;x=\"y\"";

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
    public void SqlServerAndMySql_AreAvailable(TargetDatabaseEngine engine) {
        var profile = new TargetEngineCatalog(AllProfiles()).For(engine);

        Assert.True(profile.IsAvailable);
        Assert.Null(profile.UnavailableReason);
    }

    private static SqlServerEngineProfile SqlServer() => new(NullLoggerFactory.Instance);

    private static TargetConnectionDetails SqlServerDetails(string password = HostilePassword,
        IReadOnlyDictionary<string, string>? options = null) => new() {
        Engine = TargetDatabaseEngine.SqlServer,
        Host = "sql.example.internal",
        Port = 1434,
        DatabaseName = "warehouse",
        Username = "loader",
        Password = password,
        Options = options ?? new Dictionary<string, string>()
    };

    [Fact]
    public void SqlServer_APasswordFullOfDelimiters_RoundTripsWithoutChangingAnyOtherSetting() {
        var connectionString = SqlServer().BuildConnectionString(SqlServerDetails());

        var parsed = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal(HostilePassword, parsed.Password);
        Assert.Equal("sql.example.internal,1434", parsed.DataSource);
        Assert.Equal("warehouse", parsed.InitialCatalog);
        Assert.Equal("loader", parsed.UserID);
    }

    [Fact]
    public void SqlServer_DefaultsToEncryptedWithoutTrustingTheServerCertificate() {
        var parsed = new SqlConnectionStringBuilder(SqlServer().BuildConnectionString(SqlServerDetails()));

        Assert.True(parsed.Encrypt);
        Assert.False(parsed.TrustServerCertificate);
    }

    [Fact]
    public void SqlServer_EncryptionOptions_LandOnTheDriverSettings() {
        var details = SqlServerDetails(options: new Dictionary<string, string> {
            ["encrypt"] = "false",
            ["trustServerCertificate"] = "true"
        });

        var parsed = new SqlConnectionStringBuilder(SqlServer().BuildConnectionString(details));

        Assert.False(parsed.Encrypt);
        Assert.True(parsed.TrustServerCertificate);
    }

    private static MySqlEngineProfile MySql() => new(NullLoggerFactory.Instance);

    private static TargetConnectionDetails MySqlDetails(string password = HostilePassword,
        IReadOnlyDictionary<string, string>? options = null) => new() {
        Engine = TargetDatabaseEngine.MySql,
        Host = "mysql.example.internal",
        Port = 3307,
        DatabaseName = "warehouse",
        Username = "loader",
        Password = password,
        Options = options ?? new Dictionary<string, string>()
    };

    [Fact]
    public void MySql_APasswordFullOfDelimiters_RoundTripsWithoutChangingAnyOtherSetting() {
        var connectionString = MySql().BuildConnectionString(MySqlDetails());

        var parsed = new MySqlConnectionStringBuilder(connectionString);
        Assert.Equal(HostilePassword, parsed.Password);
        Assert.Equal("mysql.example.internal", parsed.Server);
        Assert.Equal(3307u, parsed.Port);
        Assert.Equal("warehouse", parsed.Database);
        Assert.Equal("loader", parsed.UserID);
    }

    [Fact]
    public void MySql_DefaultsToPreferredSslMode() {
        var parsed = new MySqlConnectionStringBuilder(MySql().BuildConnectionString(MySqlDetails()));

        Assert.Equal(MySqlSslMode.Preferred, parsed.SslMode);
    }

    [Fact]
    public void MySql_SslModeOption_LandsOnTheDriverSetting() {
        var details = MySqlDetails(options: new Dictionary<string, string> { ["sslMode"] = "Required" });

        var parsed = new MySqlConnectionStringBuilder(MySql().BuildConnectionString(details));

        Assert.Equal(MySqlSslMode.Required, parsed.SslMode);
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
