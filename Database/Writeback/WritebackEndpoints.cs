namespace Database.Writeback;

/// <summary>
/// The two logins Writeback uses: the Target Connection's own, which writes rows and sets the database up, and
/// the Replication Credential, which only reads changes.
/// </summary>
public sealed record WritebackEndpoints(string ConnectionString, string ReplicationConnectionString);
