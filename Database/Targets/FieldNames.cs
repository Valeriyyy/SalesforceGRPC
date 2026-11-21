namespace Database.Targets;

/// <summary>
/// The field names that map onto typed columns rather than the options document.
/// </summary>
public static class FieldNames {
    public const string Host = "host";
    public const string Port = "port";
    public const string DatabaseName = "databaseName";
    public const string Username = "username";
    public const string Password = "password";
    public const string FilePath = "filePath";

    public static readonly IReadOnlySet<string> Typed = new HashSet<string>(StringComparer.Ordinal) {
        Host, Port, DatabaseName, Username, Password, FilePath
    };
}
