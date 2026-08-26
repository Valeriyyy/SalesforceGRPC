using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

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

/// <summary>
/// Thrown when stored ciphertext will not decrypt with the protecting key that is present.
/// </summary>
/// <remarks>
/// Never conflate this with "not configured yet". A fresh install and a lost protecting key look identical
/// from the outside — no usable credentials — and the remedies are opposites. Told it is not configured, a
/// user re-runs setup, generates a new Signing Keypair, orphans the External Client App in Salesforce, and
/// buries the fact that a key ring went missing.
/// </remarks>
public sealed class SecretsUnreadableException : InvalidOperationException {
    public SecretsUnreadableException(string message, Exception? inner = null) : base(message, inner) { }
}

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

/// <inheritdoc />
public sealed class SecretProtector : ISecretProtector {
    /// <summary>
    /// The Data Protection purpose. Stable forever: changing it makes every stored secret undecryptable
    /// while looking, from the key ring's point of view, like nothing is wrong.
    /// </summary>
    public const string Purpose = "SalesforceGrpc.OrgConnection.v1";

    private readonly IDataProtector? _protector;
    private readonly ILogger<SecretProtector> _logger;

    public SecretProtector(IDataProtectionProvider? provider, string protectingKeyDescription,
        ILogger<SecretProtector> logger) {
        _protector = provider?.CreateProtector(Purpose);
        ProtectingKeyDescription = protectingKeyDescription;
        _logger = logger;
    }

    public bool IsAvailable => _protector is not null;

    public string ProtectingKeyDescription { get; }

    public string Protect(string plaintext) {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (_protector is null) {
            throw new SecretProtectionUnavailableException(
                "Secrets cannot be stored because no protecting key is available. " +
                $"Supply a protecting certificate and restart. Looked at: {ProtectingKeyDescription}.");
        }

        return _protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext) {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (_protector is null) {
            throw new SecretsUnreadableException(
                "A secret is stored but no protecting key is available to read it. This is not a fresh " +
                "install: re-running setup would generate a new Signing Keypair and orphan the External " +
                $"Client App in Salesforce. Restore the protecting certificate instead. Looked at: {ProtectingKeyDescription}.");
        }

        try {
            return _protector.Unprotect(ciphertext);
        } catch (Exception ex) {
            throw new SecretsUnreadableException(
                "A stored secret could not be decrypted with the protecting key that is present " +
                $"({ProtectingKeyDescription}). The key ring or the protecting certificate has changed. " +
                "Restore them rather than re-running setup, which would orphan the External Client App in Salesforce.",
                ex);
        }
    }

    public bool TryUnprotect(string ciphertext, out string plaintext) {
        plaintext = "";

        if (_protector is null || string.IsNullOrEmpty(ciphertext)) {
            return false;
        }

        try {
            plaintext = _protector.Unprotect(ciphertext);
            return true;
        } catch (Exception ex) {
            _logger.LogDebug(ex, "Stored ciphertext did not decrypt with the current protecting key");
            return false;
        }
    }
}
