using Application.Connections;
using Database.Models;
using Database.Targets;

namespace Application.Targets;

/// <summary>
/// The one place a stored Target Connection becomes plaintext details.
/// </summary>
public static class TargetConnectionDecryption {
    /// <exception cref="SecretsUnreadableException">The stored password will not decrypt.</exception>
    public static TargetConnectionDetails Decrypt(this TargetConnection connection, ISecretProtector protector) => new() {
        Engine = connection.Engine,
        Host = connection.Host,
        Port = connection.Port,
        DatabaseName = connection.DatabaseName,
        Username = connection.Username,
        Password = connection.PasswordEncrypted is { } cipher ? protector.Unprotect(cipher) : null,
        FilePath = connection.FilePath,
        Options = connection.Options
    };
}
