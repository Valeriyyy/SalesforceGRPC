using System.Net;
using System.Net.Http.Headers;

namespace SalesforceGrpc.Salesforce;

public class SalesforceAuthHandler : DelegatingHandler {
    private readonly ISalesforceTokenProvider _tokenProvider;

    public SalesforceAuthHandler(ISalesforceTokenProvider tokenProvider) {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        
        // request.Headers.Authorization =
        //     new AuthenticationHeaderValue("Bearer", (await _tokenProvider.GetAuthToken()).AccessToken);
        //
        // var response = await base.SendAsync(request, cancellationToken);
        //
        // // Reactive refresh
        // if (response.StatusCode == HttpStatusCode.Unauthorized)
        // {
        //     response.Dispose();
        //
        //     // Force token refresh
        //     var authToken = await _tokenProvider.GetAuthToken();
        //
        //     request.Headers.Authorization =
        //         new AuthenticationHeaderValue("Bearer", authToken.AccessToken);
        //
        //     return await base.SendAsync(request, cancellationToken);
        // }
        //
        // return response;
        
        async Task<HttpResponseMessage> SendWithTokenAsync()
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    (await _tokenProvider.GetAuthToken()).AccessToken);

            return await base.SendAsync(request, cancellationToken);
        }

        var response = await SendWithTokenAsync();

        if (!IsAuthFailure(response))
            return response;

        response.Dispose();

        await _tokenProvider.ForceRefreshAsync(cancellationToken);

        return await SendWithTokenAsync();
    }
    
    private static bool IsAuthFailure(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return true;

        // Optional: inspect Salesforce error payload
        return false;
    }
}