using Newtonsoft.Json;

namespace Salesforce.Auth;

/// <summary>
/// Posts a form to Salesforce's token endpoint and turns whatever comes back into a token or a translated
/// failure.
/// </summary>
/// <remarks>
/// Shared by the JWT Bearer flow and the Bootstrap's authorization code exchange. They send different form
/// fields, but everything after that — the endpoint, reading the body, deciding whether Salesforce agreed,
/// and translating the refusal — is identical, and had already begun to drift when it was written twice.
/// </remarks>
internal static class SalesforceTokenEndpoint {
    /// <summary>
    /// Exchanges a form for an access token.
    /// </summary>
    /// <exception cref="SalesforceOAuthException">
    /// Salesforce refused, or answered successfully with nothing usable in it. Carries the raw response body
    /// either way — the translation table is incomplete by construction, so the untouched text is the only
    /// thing that can be searched for or pasted into a support conversation.
    /// </exception>
    public static async Task<AuthToken> PostAsync(HttpClient client, SalesforceLoginHost host,
        IEnumerable<KeyValuePair<string, string>> form, string? certificateFingerprint,
        CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Post, host.TokenEndpoint) {
            Content = new FormUrlEncodedContent(form)
        };

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            throw new SalesforceOAuthException(
                OAuthErrorTranslator.Translate(body, certificateFingerprint, host.IsSandbox));
        }

        var token = JsonConvert.DeserializeObject<AuthToken>(body);
        if (token?.AccessToken is null || token.InstanceUrl is null) {
            // A 200 with nothing usable in it. Rare, and completely opaque if reported as anything vaguer.
            throw new SalesforceOAuthException(new SalesforceOAuthError {
                Error = "unusable_token_response",
                ErrorDescription = "Salesforce accepted the request but returned no access token or instance URL.",
                RawResponse = body
            });
        }

        return token;
    }
}
