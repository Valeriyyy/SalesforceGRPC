using Avro.Specific;
//using com.sforce.eventbus;
using MediatR;
//using Models.PosgresModels;
//using Models.PostgresModels;
using Newtonsoft.Json;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
using SqlKata.Execution;
using System.Dynamic;
using System.Net.Http.Headers;
using System.Text;
using static System.Console;

namespace SalesforceGrpc.Handlers.Contact;
public class ContactCreateHandler {
    public class Handler : IRequestHandler<ContactCreateCommand> {
        private readonly QueryFactory _db;
        /*private readonly ILogger<Handler> _logger;

        public Handler(QueryFactory db, ILogger<Handler> logger) {
            _db = db;
            _logger = logger;
        }*/

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(ContactCreateCommand request, CancellationToken cancellationToken) {
            /*_db.Logger = compiled => {
                _logger.LogInformation("Query {query}", compiled.ToString());
            };*/
            /*var contEvent = request.ChangeEvent as ContactChangeEvent;
            var eo = new ExpandoObject();
            var eoColl = (ICollection<KeyValuePair<string, object>>)eo;
            var contEventFieldNamesDict = contEvent.GetFieldNamesDict();
            var contGuid = Guid.NewGuid();
            // starting at 1 because the 1st element is the change event header object
            for (int i = 1; i < contEventFieldNamesDict.Count; i++) {
                var fieldName = contEventFieldNamesDict[i];
                var isValidName = ContactFieldMapping._contMappings.TryGetValue(fieldName, out var pgName);
                if (isValidName) {
                    WriteLine(fieldName);
                    var fieldValue = contEvent.Get(i);
                    KeyValuePair<string, object> kvp;
                    if (fieldValue is ISpecificRecord nestedObj) {
                        var nestedFieldNamesDict = nestedObj.GetFieldNamesDict();
                        for (int j = 0; j < nestedFieldNamesDict.Count; j++) {
                            var nestedName = fieldName + nestedFieldNamesDict[j];
                            var isValidNestedValue = ContactFieldMapping._contMappings.TryGetValue(nestedName, out pgName);
                            if (isValidNestedValue) {
                                var nestedValue = nestedObj.Get(j);
                                kvp = new KeyValuePair<string, object>(pgName, nestedValue);
                                eoColl.Add(kvp);
                            }
                        }
                        continue;
                        //} else if (fieldName == "LastModifiedDate" || fieldName == "CreatedDate") {
                    } else if (fieldValue is long @longDate) {
                        var dateTime = ConvertEpochToDateTime(@longDate);
                        kvp = new KeyValuePair<string, object>(pgName, dateTime);
                    } else if (fieldName == "GUID__c") {
                        kvp = new KeyValuePair<string, object>("guid", contGuid);
                    } else {
                        kvp = new KeyValuePair<string, object>(pgName, fieldValue);
                    }
                    eoColl.Add(kvp);
                }
            }
            var sfId = contEvent.ChangeEventHeader.recordIds[0];
            eoColl.Add(new KeyValuePair<string, object>("sf_id", sfId));
            await _db.Query("salesforce.contacts").InsertAsync(eoColl, null, null, cancellationToken);*/
        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }

        /*private async Task<SFContact> SetAccountGUID(SFContact cont) {
            var uri = new Uri("https://rcgauto--valeriybox.sandbox.my.salesforce.com");
            var sfClient = new HttpClient {
                BaseAddress = uri
            };
            sfClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Worker.token);
            var dict = new Dictionary<string, Guid?>
            {
                { "GUID__c", cont.Guid }
            };
            var patchPayload = new StringContent(JsonConvert.SerializeObject(dict), Encoding.UTF8, "application/json");
            WriteLine($"updating contact in salesforce {cont.SfId} with guid {cont.Guid}");
            try {
                var res = await sfClient.PatchAsync($"/services/data/v56.0/sobjects/Account/{cont.SfId}", patchPayload);
                WriteLine(res.StatusCode.ToString());
            } catch (Exception ex) {
                WriteLine("sfclient exception: " + ex.Message);
            }

            return cont;
        }*/
    }
}
