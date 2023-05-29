using Avro.Specific;
//using com.sforce.eventbus;
using MediatR;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
using SqlKata.Execution;
using System.Collections;
using System.Dynamic;
using static System.Console;

namespace SalesforceGrpc.Handlers.Account;
public class AccountUpdateHandler {

    public class Handler : IRequestHandler<AccountUpdateCommand> {
        private readonly QueryFactory _db;

        public Handler(QueryFactory db) {
            _db = db;
        }

        public async Task Handle(AccountUpdateCommand request, CancellationToken cancellationToken) {
            WriteLine("Handling Account Update");
            /*var updateEvent = request.ChangeEvent as AccountChangeEvent;
            var eo = new ExpandoObject();
            var eoColl = (ICollection<KeyValuePair<string, object>>)eo;
            var accEventFieldNamesDict = updateEvent.GetFieldNamesDict();
            WriteLine(updateEvent.ChangeEventHeader.changedFields.Count);
            foreach (var changedFieldHex in updateEvent.ChangeEventHeader.changedFields) {
                var changedFieldsSplitArray = changedFieldHex.Split('-');
                if (changedFieldsSplitArray.Length > 1) {
                    var avroFieldNumber = int.Parse(changedFieldsSplitArray[0]);
                    var hexBitMap = changedFieldsSplitArray[1];
                    var nestedObj = (ISpecificRecord)updateEvent.Get(avroFieldNumber);
                    var accFieldName = accEventFieldNamesDict[avroFieldNumber];
                    var reversedBitArray = GetReveresedBitArray(hexBitMap[(hexBitMap.LastIndexOf('x') + 1)..]);
                    var nestedNamesDict = nestedObj.GetFieldNamesDict();
                    for (int i = 0; i < reversedBitArray.Length; i++) {
                        if (reversedBitArray[i]) {
                            WriteLine("found true at " + i);
                            var fieldName = nestedNamesDict[i];
                            var fieldValue = nestedObj.Get(i);
                            var validField = AccountFieldMapping._accMappings.TryGetValue(accFieldName + fieldName, out var pgName);
                            WriteLine(accFieldName + " " + fieldName + " " + fieldValue);
                            if (validField) {
                                var newKvp = new KeyValuePair<string, object>(pgName, fieldValue);
                                eoColl.Add(newKvp);
                            }
                        }
                    }
                } else {
                    var hexBitMap = changedFieldsSplitArray.First();
                    var reversedBitArray = GetReveresedBitArray(hexBitMap[(hexBitMap.LastIndexOf('x') + 1)..]);
                    for (int i = 0; i < reversedBitArray.Length; i++) {
                        if (reversedBitArray[i]) {
                            WriteLine("found true at " + i);
                            var fieldName = accEventFieldNamesDict[i];
                            var fieldValue = updateEvent.Get(i);
                            WriteLine(fieldName + " " + fieldValue);
                            var validField = AccountFieldMapping._accMappings.TryGetValue(fieldName, out var pgName);
                            if (validField && pgName is not null && pgName != "guid") {
                                if (fieldName == "LastModifiedDate") {
                                    DateTime dateTime = ConvertEpochToDateTime((long)fieldValue);
                                    var newKvp = new KeyValuePair<string, object>("last_modified_date", dateTime);
                                    eoColl.Add(newKvp);
                                } else {
                                    var newKvp = new KeyValuePair<string, object>(pgName, fieldValue);
                                    eoColl.Add(newKvp);
                                }
                            } else {
                                //_logger.LogWarning("Field {f} is not a valid field and will not be updated in the db", fieldName);
                            }
                        }
                    }
                }
            }
            if (eo is not null) {
                await _db.Query("salesforce.accounts").WhereIn("sf_id", updateEvent.ChangeEventHeader.recordIds).UpdateAsync(eo, null, null, cancellationToken);
            }*/
        }

        private static BitArray GetReveresedBitArray(string byteString) {
            var bytes = Convert.FromHexString(byteString);
            Array.Reverse(bytes);
            return new BitArray(bytes);
        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }
    }
}
