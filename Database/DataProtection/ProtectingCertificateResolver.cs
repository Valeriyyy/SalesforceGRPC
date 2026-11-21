using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

namespace Database.DataProtection;

/// <inheritdoc />
/// <remarks>
/// Ordered: environment variable (the legacy name only when the current one is unset), then file, then cloud
/// KMS. The first hit wins, and every miss is recorded.
/// <para>
/// This deliberately cannot mint a certificate for itself. An application that generates its own protecting
/// key and stores it beside the data it protects has achieved nothing, and the operator will not discover
/// they were supposed to back it up until it is gone.
/// </para>
/// </remarks>
public sealed class ProtectingCertificateResolver : IProtectingCertificateResolver {
    private readonly ProtectingCertificateOptions _options;
    private readonly ILogger<ProtectingCertificateResolver> _logger;

    public ProtectingCertificateResolver(IConfiguration configuration, ILogger<ProtectingCertificateResolver> logger) {
        _logger = logger;
        _options = configuration.GetSection("DataProtection:ProtectingCertificate")
                       .Get<ProtectingCertificateOptions>()
                   ?? new ProtectingCertificateOptions();
    }

    public ProtectingCertificateResolution Resolve() {
        var attempts = new List<string>();

        if (TryFromEnvironment(attempts) is { } fromEnvironment) {
            return fromEnvironment;
        }

        if (TryFromFile(attempts) is { } fromFile) {
            return fromFile;
        }

        NoteKeyManagementService(attempts);

        return new ProtectingCertificateResolution {
            Source = "no protecting certificate was found",
            Attempts = attempts
        };
    }

    private ProtectingCertificateResolution? TryFromEnvironment(List<string> attempts) {
        var variable = _options.EnvironmentVariable;
        if (string.IsNullOrWhiteSpace(variable)) {
            attempts.Add("environment variable: not configured");
            return null;
        }

        if (ReadVariable(variable) is { } encoded && !string.IsNullOrWhiteSpace(encoded)) {
            return LoadFromEnvironment(variable, encoded, _options.PasswordEnvironmentVariable, attempts);
        }
        attempts.Add($"${variable}: unset");

        // Only an unset current variable falls back. One that is set but malformed is a mistake to report, not
        // a reason to quietly load whatever the old name still holds.
        var legacy = _options.LegacyEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(legacy)) {
            return null;
        }

        if (ReadVariable(legacy) is not { } legacyEncoded || string.IsNullOrWhiteSpace(legacyEncoded)) {
            attempts.Add($"${legacy}: unset");
            return null;
        }

        _logger.LogWarning("${Legacy} is deprecated; rename it to ${Current} (and its password variable to match)",
            legacy, variable);
        return LoadFromEnvironment(legacy, legacyEncoded, _options.LegacyPasswordEnvironmentVariable, attempts);
    }

    private ProtectingCertificateResolution? LoadFromEnvironment(string variable, string encoded,
        string? passwordVariable, List<string> attempts) {
        try {
            var password = ReadVariable(passwordVariable);

            var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(encoded), password);
            var source = $"${variable}";

            _logger.LogInformation("Data Protection key ring is protected by the certificate in {Source} ({Subject})",
                source, certificate.Subject);

            return new ProtectingCertificateResolution { Certificate = certificate, Source = source, Attempts = attempts };
        } catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException) {
            // A malformed value is a configuration mistake, not an absence — say which so the operator does
            // not go looking for an unset variable that is in fact set and wrong.
            attempts.Add($"${variable}: set, but not a loadable PFX ({ex.Message})");
            _logger.LogError(ex, "${Variable} is set but could not be loaded as a PFX", variable);
            return null;
        }
    }

    private ProtectingCertificateResolution? TryFromFile(List<string> attempts) {
        var path = _options.FilePath;
        if (string.IsNullOrWhiteSpace(path)) {
            attempts.Add("file path: not configured");
            return null;
        }

        if (!File.Exists(path)) {
            attempts.Add($"{path}: no such file");
            return null;
        }

        try {
            var password = FilePassword();

            var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);

            _logger.LogInformation("Data Protection key ring is protected by the certificate at {Path} ({Subject})",
                path, certificate.Subject);

            return new ProtectingCertificateResolution { Certificate = certificate, Source = path, Attempts = attempts };
        } catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException) {
            attempts.Add($"{path}: present, but not a loadable PFX ({ex.Message})");
            _logger.LogError(ex, "{Path} exists but could not be loaded as a PFX", path);
            return null;
        }
    }

    private string? FilePassword() {
        if (ReadVariable(_options.PasswordEnvironmentVariable) is { } password) {
            return password;
        }

        if (ReadVariable(_options.LegacyPasswordEnvironmentVariable) is { } legacy) {
            _logger.LogWarning("${Legacy} is deprecated; rename it to ${Current}",
                _options.LegacyPasswordEnvironmentVariable, _options.PasswordEnvironmentVariable);
            return legacy;
        }

        return null;
    }

    private static string? ReadVariable(string? variable) =>
        string.IsNullOrWhiteSpace(variable) ? null : Environment.GetEnvironmentVariable(variable);

    private void NoteKeyManagementService(List<string> attempts) {
        if (string.IsNullOrWhiteSpace(_options.KeyManagementServiceKeyId)) {
            attempts.Add("cloud KMS: not configured");
            return;
        }

        // Configured but unimplemented is the one case that must not pass quietly: the operator believes the
        // key ring is protected by their KMS and it is not.
        attempts.Add($"cloud KMS ({_options.KeyManagementServiceKeyId}): configured, but KMS support is not implemented");
        _logger.LogError(
            "DataProtection:ProtectingCertificate:KeyManagementServiceKeyId is set to {KeyId}, but cloud KMS support " +
            "is not implemented. Supply the certificate through the environment variable or file path instead.",
            _options.KeyManagementServiceKeyId);
    }
}
