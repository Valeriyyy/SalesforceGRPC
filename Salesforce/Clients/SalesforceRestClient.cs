using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceGrpc.Salesforce;

namespace Salesforce.Clients;

public class SalesforceRestClient : BaseSalesforceClient {
    public SalesforceRestClient(HttpClient httpClient, ISalesforceTokenProvider tokenProvider,
        IOptions<SalesforceConfig> config, ILogger<SalesforceRestClient> logger)
        : base(httpClient, config.Value, logger, tokenProvider) {
    }
}
