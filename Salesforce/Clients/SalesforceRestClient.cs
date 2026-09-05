using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Salesforce.Auth;

namespace Salesforce.Clients;

public class SalesforceRestClient : BaseSalesforceClient {
    public SalesforceRestClient(HttpClient httpClient, IOrgConnectionSource connections,
        IOptions<SalesforceConfig> config, ILogger<SalesforceRestClient> logger)
        : base(httpClient, config.Value, logger, connections) {
    }
}
