using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Database.DataProtection;

/// <summary>
/// Where to look for the certificate that protects the Data Protection key ring.
/// </summary>
/// <remarks>
/// Bound from the <c>DataProtection:ProtectingCertificate</c> section. Every setting here names a
/// <em>location</em>; none of them is key material, so this object is safe to log.
/// </remarks>
public sealed class ProtectingCertificateOptions {
    /// <summary>Environment variable holding a base64-encoded PFX. Tried first.</summary>
    public string? EnvironmentVariable { get; set; } = "STARPUSH_PROTECTING_CERT";

    /// <summary>Environment variable holding the PFX password, if it has one.</summary>
    public string? PasswordEnvironmentVariable { get; set; } = "STARPUSH_PROTECTING_CERT_PASSWORD";

    /// <summary>
    /// The pre-rename name of <see cref="EnvironmentVariable"/>, read only when that one is unset so an
    /// existing deployment keeps starting. Deprecated: using it logs a Warning.
    /// </summary>
    public string? LegacyEnvironmentVariable { get; set; } = "SALESFORCEGRPC_PROTECTING_CERT";

    /// <summary>The pre-rename name of <see cref="PasswordEnvironmentVariable"/>. Deprecated.</summary>
    public string? LegacyPasswordEnvironmentVariable { get; set; } = "SALESFORCEGRPC_PROTECTING_CERT_PASSWORD";

    /// <summary>Path to a PFX file. Tried second.</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Identifier of a key in a cloud KMS. Tried last, and not implemented — the seam exists so the third
    /// option does not require rearranging this class, and a configured value is reported rather than
    /// silently ignored.
    /// </summary>
    public string? KeyManagementServiceKeyId { get; set; }
}

/// <summary>
/// The outcome of looking for the protecting certificate: what was found, and where every attempt looked.
/// </summary>
/// <remarks>
/// <see cref="Attempts"/> carries the failure message, not just the fact of failure. "No protecting
/// certificate" tells an operator nothing; "looked at $STARPUSH_PROTECTING_CERT (unset),
/// $SALESFORCEGRPC_PROTECTING_CERT (unset) and /etc/certs/dp.pfx (no such file)" tells them what to do next.
/// </remarks>
public sealed record ProtectingCertificateResolution {
    public X509Certificate2? Certificate { get; init; }

    /// <summary>Where the certificate came from, or a summary of where it was not.</summary>
    public required string Source { get; init; }

    public IReadOnlyList<string> Attempts { get; init; } = [];

    public bool Resolved => Certificate is not null;
}

/// <summary>
/// Resolves the certificate that protects the Data Protection key ring, from outside the database.
/// </summary>
public interface IProtectingCertificateResolver {
    ProtectingCertificateResolution Resolve();
}

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
