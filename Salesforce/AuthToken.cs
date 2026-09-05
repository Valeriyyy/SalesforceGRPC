using Newtonsoft.Json;

namespace Salesforce;

public record AuthToken {
    [JsonProperty("access_token")]
    public string? AccessToken { get; set; }
    [JsonProperty("signature")]
    public string? Signature { get; set; }
    [JsonProperty("instance_url")]
    public string? InstanceUrl { get; set; }
    [JsonProperty("scope")]
    public string? Scope { get; set; }
    [JsonProperty("token_type")]
    public string? TokenType { get; set; }
    [JsonProperty("issued_at")]
    public string? IssuedAt { get; set; }

    /// <summary>
    /// The identity URL, e.g. <c>https://login.salesforce.com/id/00Dxx0000001gPL/005xx000001Svog</c>.
    /// </summary>
    [JsonProperty("id")]
    public string? Id { get; set; }

    /// <summary>Refresh token, present only for the authorization code flow used during the Bootstrap.</summary>
    /// <remarks>
    /// The JWT Bearer flow issues none, and does not need one: every token request signs a fresh assertion.
    /// </remarks>
    [JsonProperty("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Seconds until expiry, when Salesforce sends it.
    /// </summary>
    /// <remarks>
    /// The JWT Bearer flow does not reliably return this — session length is an org setting rather than a
    /// property of the grant — so callers need a fallback. It is read here so that when Salesforce does say,
    /// its answer wins over any guess of ours.
    /// </remarks>
    [JsonProperty("expires_in")]
    public int? ExpiresInSeconds { get; set; }

    /// <summary>
    /// The org id from the identity URL, or null when the URL is absent or not the expected shape.
    /// </summary>
    /// <remarks>
    /// The identity URL is <c>.../id/{orgId}/{userId}</c>, so the org id is the second-to-last segment. This
    /// is the application's only source for the value and it feeds the Pub/Sub <c>tenantid</c> header, so a
    /// null here is worth reporting rather than defaulting past.
    /// </remarks>
    [JsonIgnore]
    public string? OrgId {
        get {
            if (string.IsNullOrWhiteSpace(Id) || !Uri.TryCreate(Id, UriKind.Absolute, out var identityUrl)) {
                return null;
            }

            var segments = identityUrl.Segments
                .Select(segment => segment.Trim('/'))
                .Where(segment => segment.Length > 0)
                .ToArray();

            return segments.Length >= 3 && segments[^3].Equals("id", StringComparison.OrdinalIgnoreCase)
                ? segments[^2]
                : null;
        }
    }
};
