namespace DTO;

/// <summary>
/// Confirms a Disconnect, which is the only destructive action in the application.
/// </summary>
/// <remarks>
/// The confirmation names counts rather than being a bare boolean, so a caller that has not looked at what it
/// is destroying cannot satisfy it by accident. The user is losing hand-built mapping work.
/// </remarks>
public record ConfirmDisconnectDTO {
    /// <summary>Must match the Binding count reported by the Disconnect preview.</summary>
    public int ExpectedBindings { get; set; }

    /// <summary>Must match the Field Mapping count reported by the Disconnect preview.</summary>
    public int ExpectedFieldMappings { get; set; }

    /// <summary>Must be true when the preview named a Target Connection, and false when it did not.</summary>
    public bool ExpectedTargetConnection { get; set; }
}
