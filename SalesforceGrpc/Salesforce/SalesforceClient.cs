using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SalesforceGrpc.Salesforce.MetadataType;
using System.Net.Http.Headers;

namespace SalesforceGrpc.Salesforce;

public class SalesforceClient {
    private readonly HttpClient client;
    private readonly SalesforceConfig configuration;

    public SalesforceClient(HttpClient httpClient, IOptions<SalesforceConfig> configurationOptions) {
        client = httpClient;
        client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "00DDp000001y5Hb!ARMAQMRCrhCE808OrTyWojHGYA7C0h9SkTcXxr8iMEgQcA_PPkyY7pOGaBdoX2W7PlSfX0jTqx.PVfUDjuW3FayVTIbVDJSG");
        configuration = configurationOptions.Value;

        /*if (SalesforceAuthClient.accessToken is not null) {
            Console.WriteLine("SETTING SALESFORCE ACCESS TOKEN WOOOOOTTT");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", SalesforceAuthClient.accessToken);
        } else {
            Console.WriteLine("NOT SETTING SALESFORCE ACCESS TOKEN FFFFFFFFFUUUUUUUUU");
        }*/
    }

    public async Task GetRecordTypes() {
        var query = "select Id, Name, DeveloperName, IsActive, IsPersonType, SObjectType from RecordType where sObjectType IN ('Account', 'Contact', 'Some_Custom_Object__c')";
        var endpoint = $"services/data/v{configuration.ApiVersion}/query/?q={query}";
        var recordTypesString = await client.GetStringAsync(endpoint);
        var recordTypes = JsonConvert.DeserializeObject<List<RecordType>>(recordTypesString);
        Console.WriteLine("this is record types " + recordTypesString);
        //await Console.Out.WriteLineAsync(recordTypes.Count);
    }
}
