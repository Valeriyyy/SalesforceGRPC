using Database.Models;
using Database.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace SalesforceGRPCTest;

/// <summary>
/// Integration tests for the Org Connection's App Database store, against a real Postgres.
/// </summary>
/// <remarks>
/// The connection string comes from <c>SALESFORCEGRPC_TEST_APP_DATABASE</c>, and every test skips when it is
/// unset. <b>These tests Disconnect</b>: they destroy the Org Connection and every org-scoped row in that
/// database. Point the variable at a throwaway App Database with migrations 002–006 applied, never at a real one.
/// </remarks>
public class OrgConnectionRepositoryTests {
    private const string ConnectionStringVariable = "SALESFORCEGRPC_TEST_APP_DATABASE";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OrgConnectionRepository Repository() {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
            $"Set {ConnectionStringVariable} to a throwaway App Database to run this test. It destroys what is there.");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:appDatabase"] = connectionString })
            .Build();
        return new OrgConnectionRepository(NullLogger<OrgConnectionRepository>.Instance, configuration);
    }

    private static OrgConnection Connection() => new() {
        ConsumerKey = "3MVG9key",
        AdministeringUsername = "admin@example.com",
        RunAsUsername = "integration@example.com",
        SigningPrivateKey = "ciphertext",
        SigningCertificate = "-----BEGIN CERTIFICATE-----",
        CertificateFingerprint = "AA:BB",
        CertificateExpiresAt = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task TheSelfConfigurationOutcome_IsWrittenByABootstrap_OverwrittenByTheNext_AndGoneAfterDisconnect() {
        var repository = Repository();
        await repository.DeleteConnectionAndOrgScopedStateAsync(Ct);
        await repository.UpsertAsync(Connection(), Ct);

        var first = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
        await repository.SaveSelfConfigurationAsync(false, "Not configured.", ["Upload the certificate.", "Assign the permission set."], first, Ct);

        var afterFirst = await repository.GetAsync(Ct);
        Assert.Equal(first, afterFirst!.SelfConfigurationAt?.ToUniversalTime());
        Assert.False(afterFirst.SelfConfigurationConfigured);
        Assert.Equal("Not configured.", afterFirst.SelfConfigurationSummary);
        Assert.Equal(["Upload the certificate.", "Assign the permission set."], afterFirst.SelfConfigurationManualSteps);

        var second = first.AddMinutes(5);
        await repository.SaveSelfConfigurationAsync(true, "Configured.", [], second, Ct);

        var afterSecond = await repository.GetAsync(Ct);
        Assert.Equal(second, afterSecond!.SelfConfigurationAt?.ToUniversalTime());
        Assert.True(afterSecond.SelfConfigurationConfigured);
        Assert.Empty(afterSecond.SelfConfigurationManualSteps);

        await repository.DeleteConnectionAndOrgScopedStateAsync(Ct);
        Assert.Null(await repository.GetAsync(Ct));
    }
}
