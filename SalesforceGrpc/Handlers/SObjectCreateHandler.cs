using Avro.Generic;
using com.sforce.eventbus;
using MediatR;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
using System.Dynamic;
using System.Xml.Linq;

namespace SalesforceGrpc.Handlers;
public class SObjectCreateHandler {

    public class Handler : IRequestHandler<CreateCommand> {
        private readonly IPGRepository _db;

        public Handler(IPGRepository db) {
            _db = db;
        }

        // have to figure out a way to get a mapping of fields we care about into here
        public async Task Handle(CreateCommand request, CancellationToken cancellationToken) {
            await Console.Out.WriteLineAsync("Records have been created");
            var sfRecord = request.ChangeEvent;
            // Should be the mapped fields for the current relevant entity
            var mappedFields = await _db.GetAllMappedFieldsAsync(request.EntityName, cancellationToken);
            foreach (var field in mappedFields) {
                Console.WriteLine(field.ToString());
            }
            sfRecord.GetTypedValue<GenericRecord>("ChangeEventHeader", out var changeEventHeader);
            Console.WriteLine("creating record with name " + sfRecord.GetValue(1).ToString());
            var fields = sfRecord.Schema.Fields;
            /*for (int i = 1; i < fields.Count; i++) {
                var field = fields[i];
                var val = sfRecord.GetValue(i);
                await Console.Out.WriteLineAsync(field.Name + " " + val?.ToString());
                var fieldIsNested = val is GenericRecord;
                await Console.Out.WriteLineAsync("value is nested object: " + fieldIsNested);
                if (fieldIsNested) {
                    var nestedObj = val as GenericRecord;
                    var nestedFields = nestedObj.Schema.Fields;
                    for (int j = 0; j < nestedFields.Count; j++) {
                        var nestedField = nestedFields[j];
                        var nestedValue = nestedObj.GetValue(j);
                        await Console.Out.WriteLineAsync(nestedField.Name + " " + nestedValue?.ToString());
                    }
                }
            }*/
            var eo = new ExpandoObject();
            var eoColl = (ICollection<KeyValuePair<string, object>>)eo;

            foreach (var field in mappedFields) {
                Console.WriteLine(field.SalesforceFieldName + " -> " + field.PostgresFieldName);
                var isValid = sfRecord.TryGetValue(field.SalesforceFieldName, out var val);
                if (isValid && val is not null) {
                    Console.WriteLine(val.ToString());
                    var kvp = new KeyValuePair<string, object>(field.PostgresFieldName, val);
                    eoColl.Add(kvp);
                }
            }
            changeEventHeader.GetTypedValue<object[]>("recordIds", out var recordIds);
            foreach (var recordId in recordIds) {
                Console.WriteLine(recordId);
            }
            eoColl.Add(new KeyValuePair<string, object>("sf_id", recordIds[0]));
            eoColl.Add(new KeyValuePair<string, object>("guid", Guid.NewGuid()));
            eoColl.Add(new KeyValuePair<string, object>("created_date", DateTime.UtcNow));
            await _db.InsertNewRecord(eoColl, cancellationToken);
        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }
    }
}
