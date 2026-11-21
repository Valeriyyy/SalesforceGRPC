namespace Application.Connections;

/// <summary>
/// Thrown when something asks to encrypt a secret but no protecting key is available.
/// </summary>
/// <remarks>
/// A distinct type because the answer is always the same and always the operator's: supply a protecting
/// certificate. Letting this surface as a generic failure would send a user looking at their Salesforce
/// setup, which is fine.
/// </remarks>
public sealed class SecretProtectionUnavailableException : InvalidOperationException {
    public SecretProtectionUnavailableException(string message) : base(message) { }
}
