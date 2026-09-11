using Database.Models;

namespace Database.Targets;

/// <summary>
/// A Target Connection's details in plaintext, as an engine profile consumes them.
/// </summary>
/// <remarks>
/// This is the only type that carries the password decrypted. It is built when a connection string is needed
/// and dropped afterwards; it is never stored, cached or returned by the API.
/// </remarks>
public sealed record TargetConnectionDetails {
    public required TargetDatabaseEngine Engine { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? DatabaseName { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? FilePath { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
}
