using Microsoft.Extensions.Options;

namespace SalesforceGrpc.Salesforce;

public class SalesforceClient {
    private readonly HttpClient client;
    private readonly SalesforceConfig configuration;

    public SalesforceClient(HttpClient httpClient, IOptions<SalesforceConfig> configurationOptions) {
        client = httpClient;
        // client.DefaultRequestHeaders.Authorization =
        //         new AuthenticationHeaderValue("Bearer", "00DDp000001y5Hb!ARMAQJDINYLfXu5fDoKFg2RP420ppcN9c1IqI3O6_bRsDV3O0KH903sXQYjXpH8wA8UDReKrcJducs0vPYBkF2vH0irauKbr");
        configuration = configurationOptions.Value;

        /*if (SalesforceAuthClient.accessToken is not null) {
            Console.WriteLine("SETTING SALESFORCE ACCESS TOKEN WOOOOOTTT");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", SalesforceAuthClient.accessToken);
        } else {
            Console.WriteLine("NOT SETTING SALESFORCE ACCESS TOKEN FFFFFFFFFUUUUUUUUU");
        }*/
    }
}
