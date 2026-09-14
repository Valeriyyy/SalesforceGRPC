using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace Database.Targets;

/// <summary>
/// The parts of an engine profile that are the same for every engine: validation against the field
/// definitions, and the logger and debug flag the repositories need.
/// </summary>
public abstract class TargetEngineProfile : ITargetEngineProfile {
    protected readonly ILoggerFactory LoggerFactory;
    protected readonly bool DebugQuery;

    protected TargetEngineProfile(ILoggerFactory loggerFactory, bool debugQuery = false) {
        LoggerFactory = loggerFactory;
        DebugQuery = debugQuery;
    }

    public abstract TargetDatabaseEngine Engine { get; }
    public virtual bool IsAvailable => true;
    public virtual string? UnavailableReason => null;
    public abstract IReadOnlyList<FieldDefinition> Fields { get; }

    public IReadOnlyList<string> Validate(TargetConnectionDetails details) {
        var errors = new List<string>();

        foreach (var field in Fields) {
            var value = ValueOf(details, field.Name);
            if (string.IsNullOrWhiteSpace(value)) {
                if (field.Required) {
                    errors.Add($"{field.Label} is required.");
                }
                continue;
            }

            switch (field.Kind) {
                case FieldKind.Int when !int.TryParse(value, out _):
                    errors.Add($"{field.Label} must be a whole number; '{value}' is not.");
                    break;
                case FieldKind.Bool when !bool.TryParse(value, out _):
                    errors.Add($"{field.Label} must be true or false; '{value}' is not.");
                    break;
                case FieldKind.Choice when field.Choices is { } choices && !choices.Contains(value, StringComparer.OrdinalIgnoreCase):
                    errors.Add($"{field.Label} must be one of {string.Join(", ", choices)}; '{value}' is not.");
                    break;
            }
        }

        // An option the builder would not map is dropped silently by the driver, so it is rejected here.
        var optionNames = Fields.Select(f => f.Name).Where(n => !FieldNames.Typed.Contains(n)).ToHashSet(StringComparer.Ordinal);
        foreach (var key in details.Options.Keys) {
            if (FieldNames.Typed.Contains(key)) {
                errors.Add($"'{key}' is a field of its own, not an option.");
            } else if (!optionNames.Contains(key)) {
                errors.Add($"'{key}' is not an option {Engine} understands.");
            }
        }

        return errors;
    }

    private static string? ValueOf(TargetConnectionDetails details, string fieldName) => fieldName switch {
        FieldNames.Host => details.Host,
        FieldNames.Port => details.Port?.ToString(),
        FieldNames.DatabaseName => details.DatabaseName,
        FieldNames.Username => details.Username,
        FieldNames.Password => details.Password,
        FieldNames.FilePath => details.FilePath,
        _ => details.Options.GetValueOrDefault(fieldName)
    };

    public abstract string BuildConnectionString(TargetConnectionDetails details);
    public abstract IRepository CreateRepository(string connectionString);
}
