namespace Application.Connections;

/// <summary>
/// Encrypts and decrypts the secrets the Org Connection stores.
/// </summary>
public interface ISecretProtector {
    /// <summary>True when a protecting key resolved and secrets can be stored.</summary>
    bool IsAvailable { get; }

    /// <summary>Where the protecting key came from, or where it was looked for. Never key material.</summary>
    string ProtectingKeyDescription { get; }

    /// <summary>Encrypts a secret for storage.</summary>
    /// <exception cref="SecretProtectionUnavailableException">No protecting key is available.</exception>
    string Protect(string plaintext);

    /// <summary>Decrypts stored ciphertext.</summary>
    /// <exception cref="SecretsUnreadableException">The ciphertext will not decrypt.</exception>
    string Unprotect(string ciphertext);

    /// <summary>Decrypts stored ciphertext, reporting failure rather than throwing.</summary>
    bool TryUnprotect(string ciphertext, out string plaintext);
}
