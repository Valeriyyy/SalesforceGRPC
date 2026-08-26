using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Salesforce.Auth;

namespace Salesforce.Clients;

public class SalesforceRestClient : BaseSalesforceClient {
    public SalesforceRestClient(HttpClient httpClient, ISalesforceCredentialSource credentials,
        IOptions<SalesforceConfig> config, ILogger<SalesforceRestClient> logger)
        : base(httpClient, config.Value, logger, credentials) {
    }
}
