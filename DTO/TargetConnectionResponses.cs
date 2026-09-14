namespace DTO;

/// <summary>
/// The Target Connection as the API reports it.
/// </summary>
/// <remarks>
/// Every field here is safe to show. The password is absent by construction rather than by filtering, so a
/// later edit cannot accidentally reintroduce it; <see cref="HasPassword"/> says whether one is stored.
/// Everything else is returned in full because the user must be able to review and edit it.
/// </remarks>
public record TargetConnectionDTO {
    /// <summary>False on a fresh install. Every other field is then empty.</summary>
    public bool Exists { get; set; }

    /// <summary>"Incomplete", "Connected" or "Failed".</summary>
    public string ConnectionState { get; set; } = "Incomplete";

    public string Engine { get; set; } = "";
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? Username { get; set; }
    public string? FilePath { get; set; }
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.Ordinal);

    public bool HasPassword { get; set; }

    public DateTime? LastConnectedAt { get; set; }

    /// <summary>The last failure, driver text included. Null when the connection is healthy.</summary>
    public TargetConnectionFailureDTO? LastError { get; set; }

    /// <summary>Whether stored secrets can currently be read.</summary>
    public SecretProtectionDTO SecretProtection { get; set; } = new();
}

/// <summary>
/// A Target Database failure, summarised and raw.
/// </summary>
public record TargetConnectionFailureDTO {
    /// <summary>The one-line summary.</summary>
    public string Message { get; set; } = "";

    /// <summary>The driver's message, untouched. The only text a user can search for.</summary>
    public string RawResponse { get; set; } = "";

    public DateTime? OccurredAt { get; set; }
}

/// <summary>
/// One Target Database Engine: whether it can be used, and which fields it asks for.
/// </summary>
/// <remarks>
/// This is what makes a setup form renderable from data. A fifth engine is a new entry here, not a new form.
/// Unavailable engines are listed with their reason rather than omitted, so a client can explain why an
/// option is greyed out.
/// </remarks>
public record EngineDefinitionDTO {
    public string Engine { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
    public List<FieldDefinitionDTO> Fields { get; set; } = [];
}

public record FieldDefinitionDTO {
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>"String", "Int", "Bool", "Secret" or "Choice".</summary>
    public string Kind { get; set; } = "";
    public bool Required { get; set; }
    public string? Default { get; set; }

    /// <summary>The allowed values, for a Choice. Null otherwise.</summary>
    public List<string>? Choices { get; set; }
}

/// <summary>What a repoint would destroy, so the user can be asked before it happens.</summary>
public record RepointPreviewDTO {
    public int Bindings { get; set; }
    public int FieldMappings { get; set; }
}
