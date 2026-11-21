namespace StarPush.ViewModels;

/// <summary>The Signing Certificate, for comparing against Setup and for Manual Registration.</summary>
public sealed record CertificateView(string Fingerprint, DateTime? ExpiresAt, string DownloadHref);
