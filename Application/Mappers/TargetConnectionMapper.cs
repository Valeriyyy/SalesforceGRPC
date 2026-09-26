using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using DTO;

namespace Application.Mappers;

/// <summary>
/// Turns the Target Connection, its engines and its repoint preview into DTOs, and validated details into
/// the stored model.
/// </summary>
public static class TargetConnectionMapper {
    /// <remarks>
    /// Takes a null connection, because "none is configured" is still an answer the API gives, and the Secret
    /// Protection status matters most exactly then.
    /// </remarks>
    public static TargetConnectionDTO ToDto(this TargetConnection? connection, SecretProtectionDTO secretProtection) {
        if (connection is null) {
            return new TargetConnectionDTO { Exists = false, SecretProtection = secretProtection };
        }

        return new TargetConnectionDTO {
            Exists = true,
            SecretProtection = secretProtection,
            ConnectionState = connection.ConnectionState.ToString(),
            Engine = connection.Engine.ToString(),
            Host = connection.Host,
            Port = connection.Port,
            DatabaseName = connection.DatabaseName,
            Username = connection.Username,
            FilePath = connection.FilePath,
            Options = new Dictionary<string, string>(connection.Options, StringComparer.Ordinal),
            HasPassword = connection.PasswordEncrypted is not null,
            LastConnectedAt = connection.LastConnectedAt,
            LastError = string.IsNullOrWhiteSpace(connection.LastError) ? null : new TargetConnectionFailureDTO {
                Message = connection.LastError,
                RawResponse = connection.LastErrorRaw ?? "",
                OccurredAt = connection.LastErrorAt
            }
        };
    }

    /// <summary>The stored model for validated details, carrying the password already encrypted.</summary>
    public static TargetConnection ToModel(this TargetConnectionDetails details, string? passwordEncrypted) => new() {
        Engine = details.Engine,
        Host = details.Host,
        Port = details.Port,
        DatabaseName = details.DatabaseName,
        Username = details.Username,
        PasswordEncrypted = passwordEncrypted,
        FilePath = details.FilePath,
        Options = new Dictionary<string, string>(details.Options, StringComparer.Ordinal)
    };

    public static EngineDefinitionDTO ToDto(this ITargetEngineProfile profile) => new() {
        Engine = profile.Engine.ToString(),
        IsAvailable = profile.IsAvailable,
        UnavailableReason = profile.UnavailableReason,
        Fields = profile.Fields.Select(f => new FieldDefinitionDTO {
            Name = f.Name,
            Label = f.Label,
            Kind = f.Kind.ToString(),
            Required = f.Required,
            Default = f.Default,
            Choices = f.Choices?.ToList()
        }).ToList()
    };

    public static RepointPreviewDTO ToPreviewDto(this BindingCounts counts) => new() {
        Bindings = counts.Bindings,
        FieldMappings = counts.FieldMappings
    };
}
