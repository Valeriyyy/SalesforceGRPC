using Database.Models;
using Database.Repositories.Interfaces;
using DTO;

namespace Application.Mappers;

/// <summary>Turns the Org Connection and the Disconnect counts into DTOs.</summary>
public static class OrgConnectionMapper {
    /// <remarks>
    /// Takes a null connection, because a fresh install still needs the callback URL to register and the
    /// Secret Protection status to know whether setup can start.
    /// </remarks>
    public static OrgConnectionDTO ToDto(this OrgConnection? connection, string callbackUrl,
        SecretProtectionDTO secretProtection) {
        if (connection is null) {
            return new OrgConnectionDTO {
                Exists = false,
                CallbackUrl = callbackUrl,
                SecretProtection = secretProtection
            };
        }

        return new OrgConnectionDTO {
            Exists = true,
            ConnectionState = connection.ConnectionState.ToString(),
            ConsumerKey = connection.ConsumerKey,
            AdministeringUsername = connection.AdministeringUsername,
            RunAsUsername = connection.RunAsUsername,
            IsSandbox = connection.IsSandbox,
            OrgUrl = connection.OrgUrl,
            OrgId = connection.OrgId,
            LastConnectedAt = connection.LastConnectedAt,
            LastError = LastError(connection),
            CertificateFingerprint = connection.CertificateFingerprint,
            CertificateExpiresAt = connection.CertificateExpiresAt,
            CallbackUrl = callbackUrl,
            HasBootstrapSession = connection.BootstrapConsumerSecret is not null,
            SecretProtection = secretProtection
        };
    }

    /// <summary>What a Disconnect destroys, or destroyed, and what it leaves standing in Salesforce.</summary>
    public static DisconnectPreviewDTO ToPreviewDto(this OrgScopedStateCounts counts) => new() {
        Bindings = counts.Bindings,
        FieldMappings = counts.FieldMappings,
        AvroSchemas = counts.AvroSchemas,
        Channels = counts.Channels,
        ChannelMembers = counts.ChannelMembers,
        TargetConnection = counts.TargetConnection,
        LeftInSalesforce = [
            "The External Client App you created, along with its Consumer Key and Secret",
            "The permission set granting access to it, and its assignment to the Run-as User",
            "The Signing Certificate registered on the app"
        ]
    };

    private static OAuthFailureDTO? LastError(OrgConnection connection) {
        if (string.IsNullOrWhiteSpace(connection.LastError)) {
            return null;
        }

        // Both halves reach the user: the translated summary, and Salesforce's own words underneath it. The
        // raw text is stored separately precisely so this does not have to reconstruct it from a summary.
        return new OAuthFailureDTO {
            Error = connection.ConnectionState.ToString(),
            ErrorDescription = connection.LastError,
            RawResponse = connection.LastErrorRaw ?? "",
            OccurredAt = connection.LastErrorAt
        };
    }
}
