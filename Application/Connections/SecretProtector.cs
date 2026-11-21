using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Application.Connections;

/// <inheritdoc />
public sealed class SecretProtector : ISecretProtector {
    /// <summary>
    /// The Data Protection purpose. Stable forever: changing it makes every stored secret undecryptable
    /// while looking, from the key ring's point of view, like nothing is wrong.
    /// </summary>
    // Legacy product name; must not change — stored secrets are bound to it.
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
