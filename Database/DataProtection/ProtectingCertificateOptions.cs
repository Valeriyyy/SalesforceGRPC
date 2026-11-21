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
