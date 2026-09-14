using Database.Models;
using Database.Repositories.Interfaces;

namespace Database.Targets;

/// <summary>
/// Everything this application knows about one Target Database Engine, in one place.
/// </summary>
/// <remarks>
/// Which fields the user is asked for, how they become a connection string, and which repository runs against
/// it. One class per engine rather than three switch statements over the same enum, so the compiler enforces
/// that a new engine is added completely: the fifth engine cannot be added to two lists and missed from the
/// third.
/// </remarks>
public interface ITargetEngineProfile {
    TargetDatabaseEngine Engine { get; }

    /// <summary>False while the engine's repository is unimplemented. The reason is for the user.</summary>
    bool IsAvailable { get; }
    string? UnavailableReason { get; }

    IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>Problems with the supplied details, judged against <see cref="Fields"/>. Empty when valid.</summary>
    IReadOnlyList<string> Validate(TargetConnectionDetails details);

    /// <summary>
    /// Assembles a connection string through the driver's own builder.
    /// </summary>
    /// <remarks>
    /// Never by concatenation. A password containing a semicolon would otherwise end the password early and
    /// inject whatever follows as a connection-string keyword. The result is handed to a connection and
    /// dropped; nothing logs it.
    /// </remarks>
    string BuildConnectionString(TargetConnectionDetails details);

    IRepository CreateRepository(string connectionString);
}
