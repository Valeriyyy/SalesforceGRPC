using Database.Models;
using Database.Repositories.Interfaces;
using DTO;
using Salesforce.Auth;

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
            HasConsumerSecret = connection.BootstrapConsumerSecret is not null,
            IsApproved = connection.BootstrapRefreshToken is not null,
            SelfConfiguration = connection.SelfConfigurationAt is { } at
                ? new SelfConfigurationDTO {
                    At = at,
                    Configured = connection.SelfConfigurationConfigured ?? false,
                    Summary = connection.SelfConfigurationSummary ?? "",
                    ManualSteps = [.. connection.SelfConfigurationManualSteps]
                }
                : null,
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

        // Only the one-line summary and Salesforce's raw body are stored. Translating the raw body again gives
        // back its error, description and guidance as separate fields, which is how the page shows every
        // Salesforce failure — and it picks up any guidance added to the table since the failure was recorded.
        if (!string.IsNullOrWhiteSpace(connection.LastErrorRaw)) {
            var translated = OAuthErrorTranslator.Translate(connection.LastErrorRaw,
                connection.CertificateFingerprint, connection.IsSandbox);

            return new OAuthFailureDTO {
                Error = translated.Error,
                ErrorDescription = translated.ErrorDescription,
                Guidance = translated.Guidance,
                RawResponse = connection.LastErrorRaw,
                OccurredAt = connection.LastErrorAt
            };
        }

        // A failure this application raised itself (no org id in a token response) has no Salesforce body; its
        // summary, "error: description", is all there is.
        var separator = connection.LastError.IndexOf(": ", StringComparison.Ordinal);
        return new OAuthFailureDTO {
            Error = separator > 0 ? connection.LastError[..separator] : "error",
            ErrorDescription = separator > 0 ? connection.LastError[(separator + 2)..] : connection.LastError,
            RawResponse = "",
            OccurredAt = connection.LastErrorAt
        };
    }
}
