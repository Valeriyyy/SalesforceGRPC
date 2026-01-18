using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace SalesforceGrpc.Salesforce;

public class SalesforceAuthClient {
    private readonly HttpClient client;
    private readonly SalesforceConfig configuration;
    public static string accessToken = "00DDp000001y5Hb!ARMAQJB11_wX0r0nBqZHx4IjVDrdYhD2979z.Vm3badrrG8YCgAKgL8vegwS3RFG6SDbggIHGCZF8o_W3JT11y4m0uDWqIWI";

    public SalesforceAuthClient(HttpClient httpClient, IOptions<SalesforceConfig> configurationOptions) {
        client = httpClient;
        configuration = configurationOptions.Value;
    }

    public async Task<AuthToken> GetToken() {
        var nvc = new List<KeyValuePair<string, string>>
        {
            new("grant_type", configuration.GrantType),
            new("client_id", configuration.ClientId),
            new("client_secret", configuration.ClientSecret),
            new("username", configuration.Username),
            new("password", configuration.Password + configuration.UserSecurityToken)
            //new KeyValuePair<string, string>("password", configuration.Password)
        };
        var req = new HttpRequestMessage(HttpMethod.Post, configuration.LoginUrl) { Content = new FormUrlEncodedContent(nvc) };
        var response = await client.SendAsync(req);
        var content = await response.Content.ReadAsStringAsync();
        await Console.Out.WriteLineAsync("This is contrent " + content);
        var authToken = JsonConvert.DeserializeObject<AuthToken>(content);
        accessToken = authToken?.AccessToken!;
        return authToken;

        /*return new AuthToken {
            AccessToken = accessToken,
            InstanceUrl = "https://rcg8-dev-ed.develop.my.salesforce.com"
        };*/

    }
}
