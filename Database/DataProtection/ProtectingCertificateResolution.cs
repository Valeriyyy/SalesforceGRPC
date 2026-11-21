using System.Security.Cryptography.X509Certificates;

namespace Database.DataProtection;

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
