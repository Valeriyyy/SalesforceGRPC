namespace StarPush.ViewModels;

/// <summary>
/// The Replication Credential: a second login on the same database that only reads its changes, for Writeback.
/// Its password is never shown.
/// </summary>
public sealed record ReplicationCredentialView(string Username, string State, DateTime? LastProvedAt, string? LastError,
    DateTime? LastErrorAt);
