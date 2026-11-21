namespace DTO;

/// <summary>
/// The Replication Credential: a second login on the same database, used only to read its changes for
/// Writeback. A blank password keeps the stored one when the username is unchanged.
/// </summary>
public record SetReplicationCredentialDTO {
    public string Username { get; set; } = "";
    public string? Password { get; set; }
}
