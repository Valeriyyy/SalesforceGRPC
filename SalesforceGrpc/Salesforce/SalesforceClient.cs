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
                new AuthenticationHeaderValue("Bearer", "00DDp000001y5Hb!ARMAQJDINYLfXu5fDoKFg2RP420ppcN9c1IqI3O6_bRsDV3O0KH903sXQYjXpH8wA8UDReKrcJducs0vPYBkF2vH0irauKbr");
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
        Console.WriteLine("Record Types response " + recordTypesString);
        var recordTypes = JsonConvert.DeserializeObject<RecordTypeQueryResponse>(recordTypesString);
        Console.WriteLine("this is record types " + recordTypesString);
        Console.WriteLine(recordTypes.TotalSize);
        foreach (var recordType in recordTypes.Records) {
            Console.WriteLine(recordType.SObjectType + " " + recordType.Name);
        }
    }
}
