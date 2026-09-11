namespace DTO;

/// <summary>
/// The Target Connection details a user supplies.
/// </summary>
/// <remarks>
/// Decomposed rather than a connection string, so the fields can be validated against the engine's
/// definitions and rendered as a form. Which of these apply depends on the engine — the definitions endpoint
/// says which. The password is required on every save, including edits: the details are proved before they
/// are stored, and the proof needs the credential.
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

/// <summary>
/// Points the application at a different database, destroying every Binding.
/// </summary>
/// <remarks>
/// The confirmation names counts rather than being a bare boolean, so a caller that has not looked at the
/// preview cannot satisfy it by accident, and a Binding created since the preview aborts rather than vanishes.
/// </remarks>
public record RepointTargetConnectionDTO : SaveTargetConnectionDTO {
    /// <summary>Must match the Binding count reported by the repoint preview.</summary>
    public int ExpectedBindings { get; set; }

    /// <summary>Must match the Field Mapping count reported by the repoint preview.</summary>
    public int ExpectedFieldMappings { get; set; }
}
