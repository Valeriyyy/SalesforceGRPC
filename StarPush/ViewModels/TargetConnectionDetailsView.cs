using DTO;

namespace StarPush.ViewModels;

/// <summary>Every stored detail, shown back for review and edit. The password never is; only whether one is stored.</summary>
/// <param name="Engine">The API's engine name ("Postgres", "SqlServer", ...), sent back unchanged on save.</param>
public sealed record TargetConnectionDetailsView(
    string Engine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    string? FilePath,
    IReadOnlyDictionary<string, string> Options,
    bool HasPassword,
    DateTime? LastConnectedAt,
    TargetDatabaseErrorView? LastError,
    ReplicationCredentialView? Replication,
    bool WritebackArmed) {

    public static TargetConnectionDetailsView From(TargetConnectionDTO connection) => new(
        connection.Engine,
        connection.Host,
        connection.Port,
        connection.DatabaseName,
        connection.Username,
        connection.FilePath,
        connection.Options,
        connection.HasPassword,
        connection.LastConnectedAt,
        TargetDatabaseErrorView.From(connection.LastError),
        connection.Replication is { } replication
            ? new ReplicationCredentialView(replication.Username, replication.State, replication.LastProvedAt,
                replication.LastError, replication.LastErrorAt)
            : null,
        connection.WritebackArmed);
}
