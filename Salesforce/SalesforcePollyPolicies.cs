using Polly;
using System.Net;
using System;

namespace SalesforceGrpc.Salesforce;

public class SalesforcePollyPolicies {
    public static IAsyncPolicy<HttpResponseMessage> RetryWithBackoff()
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => (int)r.StatusCode >= 500)
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250))
            );
    }
}
