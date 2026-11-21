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

    /// <summary>
    /// The Replication Credential's username: a second login, used only to read the Target Database's changes
    /// for Writeback. Null until the user enters one.
    /// </summary>
    public string? ReplicationUsername { get; set; }

    public string? ReplicationPasswordEncrypted { get; set; }

    /// <summary>Whether the Replication Credential was proved to work, in the same terms as the connection.</summary>
    public ConnectionState ReplicationState { get; set; }
    public DateTime? ReplicationLastProvedAt { get; set; }
    public string? ReplicationLastError { get; set; }
    public DateTime? ReplicationLastErrorAt { get; set; }

    /// <summary>
    /// When this application set the Target Database up for Writeback, or null when it has not (or has since
    /// taken that down). While set, every write this application makes carries its tag, so Writeback never
    /// sends it back to Salesforce.
    /// </summary>
    public DateTime? WritebackArmedAt { get; set; }

    public bool HasReplicationCredential => ReplicationUsername is not null && ReplicationPasswordEncrypted is not null;

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
