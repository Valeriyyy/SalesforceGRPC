namespace DTO;

/// <summary>
/// The Target Connection details a user supplies.
/// </summary>
/// <remarks>
/// Decomposed rather than a connection string, so the fields can be validated against the engine's
/// definitions and rendered as a form. Which of these apply depends on the engine — the definitions endpoint
/// says which. On an edit that keeps the same engine, host, database name and file path, a blank or missing
/// password keeps the stored one, which the proof decrypts and uses. Anywhere else — nothing stored yet, a
/// different database, a repoint — the password is required like any other field the engine asks for.
/// </remarks>
public record SaveTargetConnectionDTO {
    /// <summary>"Postgres", "SqlServer", "MySql" or "Sqlite".</summary>
    public string Engine { get; set; } = "";

    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Sqlite only.</summary>
    public string? FilePath { get; set; }

    /// <summary>Engine-specific settings, keyed by the field names the definitions endpoint publishes.</summary>
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.Ordinal);
}
