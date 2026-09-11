namespace Database.Targets;

public enum FieldKind {
    String,
    Int,
    Bool,
    Secret,
    Choice
}

/// <summary>
/// One detail an engine asks the user for.
/// </summary>
/// <remarks>
/// Published by the API so a form can be rendered from it, and used by the same profile to validate what
/// comes back. One source of truth for both, so the form and the validator cannot disagree.
/// </remarks>
public sealed record FieldDefinition {
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required FieldKind Kind { get; init; }
    public bool Required { get; init; }
    public string? Default { get; init; }
    public IReadOnlyList<string>? Choices { get; init; }
}

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
