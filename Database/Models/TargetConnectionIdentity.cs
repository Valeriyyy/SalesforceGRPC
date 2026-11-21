namespace Database.Models;

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
