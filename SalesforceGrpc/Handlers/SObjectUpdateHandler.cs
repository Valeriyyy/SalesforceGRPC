using Avro.Generic;
using com.sforce.eventbus;
using MediatR;
using SalesforceGrpc.Database;
using SalesforceGrpc.Extensions;
using System.Dynamic;
using System.Text.Json;

namespace SalesforceGrpc.Handlers;
public class SObjectUpdateHandler {
    public class Handler : IRequestHandler<UpdateCommand> {
        private readonly IPGRepository _db;

        public Handler(IPGRepository db) {
            _db = db;
        }

        public async Task Handle(UpdateCommand request, CancellationToken cancellationToken) {
            await Console.Out.WriteLineAsync("Records have been updated");
            var sfRecord = request.ChangeEvent;
            // Should be the mapped fields for the current relevant entity
            var mappedFields = await _db.GetAllMappedFieldsAsync(request.EntityName, cancellationToken);
            foreach (var field in mappedFields) {
                Console.WriteLine(field.ToString());
            }
            //sfRecord.TryGetValue("ChangeEventHeader", out var changeEventHeaderObj);
            //var changeEventHeader = changeEventHeaderObj as GenericRecord;
            sfRecord.GetTypedValue<GenericRecord>("ChangeEventHeader", out var changeEventHeader);
            var eo = new ExpandoObject();
            var eoColl = (ICollection<KeyValuePair<string, object>>)eo;

            foreach (var field in mappedFields) {
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
            Console.WriteLine(JsonSerializer.Serialize(eoColl));
        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }
    }
}
