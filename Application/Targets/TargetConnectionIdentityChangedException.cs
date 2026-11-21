using Database.Models;

namespace Application.Targets;

/// <summary>
/// Thrown when a save would change which database the Target Connection points at.
/// </summary>
/// <remarks>
/// A conflict rather than a validation failure: the request was well formed, and the refusal is about what
/// it would destroy. Repointing is its own operation, with a preview and a confirmation, and the API maps
/// this to a response that says so.
/// </remarks>
public sealed class TargetConnectionIdentityChangedException : InvalidOperationException {
    public TargetConnectionIdentityChangedException(TargetConnectionIdentity stored, TargetConnectionIdentity requested)
        : base("These details point at a different database " +
               $"({Describe(requested)} rather than {Describe(stored)}), which would invalidate every Binding. " +
               "Use the repoint operation, which previews what will be destroyed and asks for confirmation.") { }

    private static string Describe(TargetConnectionIdentity identity) =>
        identity.FilePath is { } file ? $"{identity.Engine} {file}" : $"{identity.Engine} {identity.Host}/{identity.DatabaseName}";
}
