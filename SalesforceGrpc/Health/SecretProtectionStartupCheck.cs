using Application.Connections;

namespace SalesforceGrpc.Health;

/// <summary>
/// Says at startup whether the application can read its own stored secrets.
/// </summary>
/// <remarks>
/// Exists to keep two cases apart that naive code conflates, because the remedies are opposites:
/// <list type="bullet">
///   <item><description><b>No protecting key, nothing encrypted</b> — a fresh install. Say what to supply and carry on.</description></item>
///   <item><description><b>Ciphertext that will not decrypt</b> — a key ring or certificate went missing. This
///   must be loud, and must never present as "not configured yet": told that, the user re-runs setup,
///   generates a new Signing Keypair, orphans the External Client App in Salesforce, and buries the real cause.</description></item>
/// </list>
/// The host starts either way. The API is how the user fixes things, so refusing to boot would remove the
/// only route to a remedy.
/// </remarks>
public sealed class SecretProtectionStartupCheck : BackgroundService {
    private readonly IOrgConnectionProvider _connections;
    private readonly ISecretProtector _protector;
    private readonly ILogger<SecretProtectionStartupCheck> _logger;

    public SecretProtectionStartupCheck(IOrgConnectionProvider connections, ISecretProtector protector,
        ILogger<SecretProtectionStartupCheck> logger) {
        _connections = connections;
        _protector = protector;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        Database.Models.OrgConnection? connection;
        try {
            connection = await _connections.GetAsync(stoppingToken).ConfigureAwait(false);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            return;
        } catch (Exception ex) {
            _logger.LogError(ex, "Could not read the Org Connection at startup. The API will report the same fault.");
            return;
        }

        if (connection is null) {
            if (_protector.IsAvailable) {
                _logger.LogInformation(
                    "No Salesforce Org Connection is configured yet. Secrets can be stored; the protecting key is {ProtectingKey}.",
                    _protector.ProtectingKeyDescription);
            } else {
                _logger.LogWarning(
                    "No Salesforce Org Connection is configured, and no protecting key is available to store one. " +
                    "Supply a protecting certificate through DataProtection:ProtectingCertificate and restart. Looked at: {ProtectingKey}.",
                    _protector.ProtectingKeyDescription);
            }
            return;
        }

        if (_protector.TryUnprotect(connection.SigningPrivateKey, out _)) {
            _logger.LogInformation(
                "Salesforce Org Connection loaded ({State}); its Signing Keypair decrypted successfully.",
                connection.ConnectionState);
            return;
        }

        _logger.LogCritical(
            "A Salesforce Signing Keypair is stored but CANNOT BE DECRYPTED with the protecting key that is " +
            "present ({ProtectingKey}). This is NOT a fresh install. Restore the original protecting " +
            "certificate and Data Protection key ring. Do NOT re-run setup: it would generate a new keypair " +
            "and leave the External Client App in Salesforce orphaned, hiding the fact that a key went missing.",
            _protector.ProtectingKeyDescription);
    }
}
