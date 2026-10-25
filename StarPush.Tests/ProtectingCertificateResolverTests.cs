using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Database.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StarPush.Tests.Worker;

namespace StarPush.Tests;

/// <summary>
/// Covers finding the protecting certificate in the environment, including the pre-rename
/// <c>SALESFORCEGRPC_*</c> variables an existing deployment still sets.
/// </summary>
/// <remarks>
/// Every test names its own variables, so tests running in parallel never see each other's process-wide
/// environment.
/// </remarks>
public sealed class ProtectingCertificateResolverTests : IDisposable {
    private readonly string _suffix = Guid.NewGuid().ToString("N");
    private readonly List<string> _setVariables = [];
    private readonly ListLogger<ProtectingCertificateResolver> _log = new();

    private string Current => $"STARPUSH_TEST_CERT_{_suffix}";
    private string CurrentPassword => $"STARPUSH_TEST_CERT_PASSWORD_{_suffix}";
    private string Legacy => $"SALESFORCEGRPC_TEST_CERT_{_suffix}";
    private string LegacyPassword => $"SALESFORCEGRPC_TEST_CERT_PASSWORD_{_suffix}";

    public void Dispose() {
        foreach (var variable in _setVariables) {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private void Set(string variable, string value) {
        _setVariables.Add(variable);
        Environment.SetEnvironmentVariable(variable, value);
    }

    private ProtectingCertificateResolver Resolver(string? filePath = null) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["DataProtection:ProtectingCertificate:EnvironmentVariable"] = Current,
                ["DataProtection:ProtectingCertificate:PasswordEnvironmentVariable"] = CurrentPassword,
                ["DataProtection:ProtectingCertificate:LegacyEnvironmentVariable"] = Legacy,
                ["DataProtection:ProtectingCertificate:LegacyPasswordEnvironmentVariable"] = LegacyPassword,
                ["DataProtection:ProtectingCertificate:FilePath"] = filePath
            })
            .Build();
        return new ProtectingCertificateResolver(configuration, _log);
    }

    private static string Pfx(string subject, string? password = null) {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, password));
    }

    [Fact]
    public void TheDefaultsAreTheStarPushNamesWithTheOldNamesAsTheFallback() {
        var options = new ProtectingCertificateOptions();

        Assert.Equal("STARPUSH_PROTECTING_CERT", options.EnvironmentVariable);
        Assert.Equal("STARPUSH_PROTECTING_CERT_PASSWORD", options.PasswordEnvironmentVariable);
        Assert.Equal("SALESFORCEGRPC_PROTECTING_CERT", options.LegacyEnvironmentVariable);
        Assert.Equal("SALESFORCEGRPC_PROTECTING_CERT_PASSWORD", options.LegacyPasswordEnvironmentVariable);
    }

    [Fact]
    public void WhenBothAreSet_TheCurrentVariableWins() {
        Set(Current, Pfx("current"));
        Set(Legacy, Pfx("legacy"));

        var resolution = Resolver().Resolve();

        Assert.True(resolution.Resolved);
        Assert.Equal("CN=current", resolution.Certificate!.Subject);
        Assert.Equal($"${Current}", resolution.Source);
        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Warning);
    }

    /// <summary>
    /// A deployment from before the rename keeps working, and is told to move.
    /// </summary>
    [Fact]
    public void WhenOnlyTheLegacyVariableIsSet_ItIsUsedWithADeprecationWarning() {
        Set(Legacy, Pfx("legacy"));

        var resolution = Resolver().Resolve();

        Assert.True(resolution.Resolved);
        Assert.Equal("CN=legacy", resolution.Certificate!.Subject);
        Assert.Equal($"${Legacy}", resolution.Source);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning
                                           && e.Message.Contains(Legacy) && e.Message.Contains(Current));
    }

    /// <summary>
    /// A legacy deployment set the legacy password too; pairing it with the current password variable would
    /// fail to open its PFX.
    /// </summary>
    [Fact]
    public void TheLegacyCertificateIsOpenedWithTheLegacyPassword() {
        Set(Legacy, Pfx("legacy", "legacy-secret"));
        Set(LegacyPassword, "legacy-secret");

        var resolution = Resolver().Resolve();

        Assert.True(resolution.Resolved);
        Assert.Equal("CN=legacy", resolution.Certificate!.Subject);
    }

    [Fact]
    public void TheCurrentCertificateIsOpenedWithTheCurrentPassword() {
        Set(Current, Pfx("current", "current-secret"));
        Set(CurrentPassword, "current-secret");
        Set(LegacyPassword, "wrong");

        var resolution = Resolver().Resolve();

        Assert.True(resolution.Resolved);
        Assert.Equal("CN=current", resolution.Certificate!.Subject);
    }

    [Fact]
    public void WhenNeitherIsSet_BothAreReportedAsUnset() {
        var resolution = Resolver().Resolve();

        Assert.False(resolution.Resolved);
        Assert.Contains($"${Current}: unset", resolution.Attempts);
        Assert.Contains($"${Legacy}: unset", resolution.Attempts);
    }

    /// <summary>
    /// A deployment that keeps its PFX in a file still has only the legacy password variable set.
    /// </summary>
    [Fact]
    public void APfxFileIsOpenedWithTheLegacyPasswordWhenTheCurrentOneIsUnset() {
        var path = Path.Combine(Path.GetTempPath(), $"{_suffix}.pfx");
        File.WriteAllBytes(path, Convert.FromBase64String(Pfx("file", "legacy-secret")));
        Set(LegacyPassword, "legacy-secret");

        try {
            var resolution = Resolver(path).Resolve();

            Assert.True(resolution.Resolved);
            Assert.Equal("CN=file", resolution.Certificate!.Subject);
            Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(LegacyPassword));
        } finally {
            File.Delete(path);
        }
    }
}
