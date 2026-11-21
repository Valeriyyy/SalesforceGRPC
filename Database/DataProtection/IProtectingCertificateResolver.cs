namespace Database.DataProtection;

/// <summary>
/// Resolves the certificate that protects the Data Protection key ring, from outside the database.
/// </summary>
public interface IProtectingCertificateResolver {
    ProtectingCertificateResolution Resolve();
}
