namespace Application.Bindings;

/// <summary>
/// How well one Entity field's value fits the Target Column it is mapped to.
/// </summary>
public enum CompatibilityLevel {
    /// <summary>The value fits without loss.</summary>
    Compatible = 0,

    /// <summary>The mapping works but something is lost or unusual. Does not block activation.</summary>
    Warning = 1,

    /// <summary>The mapping cannot succeed. Blocks activation.</summary>
    Error = 2
}
