namespace Database.Models;

/// <summary>
/// The single row in salesforce.target_connection: how this application reaches the Target Database.
/// </summary>
/// <remarks>
/// <see cref="PasswordEncrypted"/> holds Data Protection ciphertext exactly as it sits in the database. Nothing
/// here decrypts it — that happens when a connection string is assembled — so an instance of this type is safe
/// to log or hand around, and a read model built from it leaks nothing as long as that one field is left out.
/// </remarks>
public class TargetConnection {
    public int Id { get; set; }

    public TargetDatabaseEngine Engine { get; set; }

    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? Username { get; set; }

    /// <summary>Data Protection ciphertext. Never plaintext.</summary>
    public string? PasswordEncrypted { get; set; }

    /// <summary>Sqlite's address. Its own field because a file path is not a database name.</summary>
    public string? FilePath { get; set; }

    public Dictionary<string, string> Options { get; set; } = new(StringComparer.Ordinal);

    public ConnectionState ConnectionState { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>The driver's message, untouched. A summary cannot be un-summarised later.</summary>
    public string? LastErrorRaw { get; set; }
    public DateTime? LastErrorAt { get; set; }

    public DateTime DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }

    /// <summary>
    /// True when the last proof succeeded and nothing has failed since. The worker streams only when this is.
    /// </summary>
    public bool IsUsable => ConnectionState == ConnectionState.Connected;

    /// <summary>
    /// The parts of a Target Connection that name <em>which</em> database it reaches. Changing any of them
    /// destroys every Binding; see docs/adr/0004.
    /// </summary>
    public TargetConnectionIdentity Identity => new(Engine, Host, DatabaseName, FilePath);

    public override string ToString() => $"{Engine} {Host ?? FilePath} {ConnectionState}";
}

/// <summary>
/// Engine, host, database name and file path: the four details that decide which database a Target
/// Connection points at. Username, password and options are not part of it — they are credentials and
/// settings for the <em>same</em> database.
/// </summary>
public sealed record TargetConnectionIdentity(
    TargetDatabaseEngine Engine, string? Host, string? DatabaseName, string? FilePath) {

    /// <summary>Case-insensitive on host, since DNS is; exact on the rest.</summary>
    public bool Equals(TargetConnectionIdentity? other) =>
        other is not null
        && Engine == other.Engine
        && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(DatabaseName, other.DatabaseName, StringComparison.Ordinal)
        && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(Engine, Host?.ToUpperInvariant(), DatabaseName, FilePath);
}
