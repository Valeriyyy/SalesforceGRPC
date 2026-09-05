using Salesforce.Auth;
using System.Net;
using System.Net.Http.Headers;

namespace Salesforce;

/// <summary>
/// Attaches a Salesforce access token to every outgoing request, refreshing once on a 401.
/// </summary>
/// <remarks>
/// Registered on the typed REST and Tooling clients, both of which do setup-level work — managing Channels
/// and Channel Members — so they authenticate as the Administering User. The event stream is the other
/// identity and does not pass through here: it authenticates through the gRPC call-credentials callback,
/// which asks for the Run-as User explicitly.
/// <para>
/// If two-way sync arrives, writing records back is Run-as User work and will need its own handler rather
/// than a widened default here.
/// </para>
/// </remarks>
public class SalesforceAuthHandler : DelegatingHandler {
    private readonly ISalesforceTokenProvider _tokenProvider;

    private const SalesforceIdentity Identity = SalesforceIdentity.AdministeringUser;

    public SalesforceAuthHandler(ISalesforceTokenProvider tokenProvider) {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        async Task<HttpResponseMessage> SendWithTokenAsync() {
            var token = await _tokenProvider.GetAuthToken(Identity, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var response = await SendWithTokenAsync().ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized) {
            return response;
        }

        // A 401 means the token was rejected, which for a JWT Bearer connection usually means the session
        // expired rather than that anything is wrong. Signing a fresh assertion is the whole remedy.
        response.Dispose();
        await _tokenProvider.ForceRefreshAsync(Identity, cancellationToken).ConfigureAwait(false);

        return await SendWithTokenAsync().ConfigureAwait(false);
    }
}
