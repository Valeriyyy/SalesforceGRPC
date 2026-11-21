namespace Application.Connections;

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
