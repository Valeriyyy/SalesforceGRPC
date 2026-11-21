namespace DTO;

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
