using Avro.Specific;
//using com.sforce.eventbus;
using MediatR;
//using Models.PostgresModels;
using Newtonsoft.Json;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
using SqlKata.Execution;
using System.Dynamic;
using System.Net.Http.Headers;
using System.Text;
using static System.Console;

namespace SalesforceGrpc.Handlers.Account;
public class AccountCreateHandler {
    public class Handler : IRequestHandler<AccountCreateCommand> {
        private readonly QueryFactory _db;
        /*private readonly ILogger<Handler> _logger;

        public Handler(QueryFactory db, ILogger<Handler> logger) {
            _db = db;
            _logger = logger;
        }*/

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(AccountCreateCommand request, CancellationToken cancellationToken) {
            /*_db.Logger = compiled => {
                _logger.LogInformation("Query {query}", compiled.ToString());
            };*/
            /*var accEvent = request.ChangeEvent as AccountChangeEvent;
            var eo = new ExpandoObject();
            var eoColl = (ICollection<KeyValuePair<string, object>>)eo;
            var accEventFieldNamesDict = accEvent.GetFieldNamesDict();
            var accGuid = Guid.NewGuid();
            // starting at 1 because the 1st element is the change event header object
            for (int i = 1; i < accEventFieldNamesDict.Count; i++) {
                var fieldName = accEventFieldNamesDict[i];
                var isValidName = AccountFieldMapping._accMappings.TryGetValue(fieldName, out var pgName);
                if (isValidName) {
                    WriteLine(fieldName);
                    var kvp = new KeyValuePair<string, object>();
                    var fieldValue = accEvent.Get(i);
                    if (fieldValue is ISpecificRecord nestedObj) {
                        var nestedFieldNamesDict = nestedObj.GetFieldNamesDict();
                        for (int j = 0; j < nestedFieldNamesDict.Count; j++) {
                            var nestedName = fieldName + nestedFieldNamesDict[j];
                            var isValidNestedValue = AccountFieldMapping._accMappings.TryGetValue(nestedName, out pgName);
                            if (isValidNestedValue) {
                                var nestedValue = nestedObj.Get(j);
                                kvp = new KeyValuePair<string, object>(pgName, nestedValue);
                                eoColl.Add(kvp);
                            }
                        }
                        continue;
                    } else if (fieldName == "LastModifiedDate" || fieldName == "CreatedDate") {
                        var dateTime = ConvertEpochToDateTime((long)fieldValue);
                        kvp = new KeyValuePair<string, object>(pgName, dateTime);
                    } else if (fieldName == "GUID__c") {
                        kvp = new KeyValuePair<string, object>("guid", accGuid);
                    } else {
                        kvp = new KeyValuePair<string, object>(pgName, fieldValue);
                    }
                    eoColl.Add(kvp);
                }
            }
            var sfId = accEvent.ChangeEventHeader.recordIds[0];
            eoColl.Add(new KeyValuePair<string, object>("sf_id", sfId));
            await _db.Query("salesforce.accounts").InsertAsync(eoColl, null, null, cancellationToken);*/
            //await SetAccountGUID(new SFAccount { SfId = sfId, Guid = accGuid });
        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }

        /*private async Task<SFAccount> SetAccountGUID(SFAccount acc) {
            var uri = new Uri("https://rcgauto--valeriybox.sandbox.my.salesforce.com");
            var sfClient = new HttpClient {
                BaseAddress = uri
            };
            sfClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Worker.token);
            var dict = new Dictionary<string, Guid?>
            {
                { "GUID__c", acc.Guid }
            };
            var patchPayload = new StringContent(JsonConvert.SerializeObject(dict), Encoding.UTF8, "application/json");
            WriteLine($"updating account in salesforce {acc.SfId} with guid {acc.Guid}");
            try {
                var res = await sfClient.PatchAsync($"/services/data/v56.0/sobjects/Account/{acc.SfId}", patchPayload);
                WriteLine(res.StatusCode.ToString());
            } catch (Exception ex) {
                WriteLine("sfclient exception: " + ex.Message);
            }

            return acc;
        }*/
    }
}
