namespace SalesforceGrpc.Salesforce;

public class SalesforceClient
{
    private readonly HttpClient client;
    private readonly SalesforceConfig configuration;

    public SalesforceClient(HttpClient httpClient, SalesforceConfig configurationOptions)
    {
        client = httpClient;
        configuration = configurationOptions;
    }
}
