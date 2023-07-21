using Avro.Generic;
using Database;
using MediatR;
using Newtonsoft.Json;
using SqlKata.Execution;
using System.Reflection;

namespace SalesforceGrpc.Handlers;
public class SObjectCreateHandler {

    public class Handler : IRequestHandler<CreateCommand> {
        private readonly QueryFactory _db;
        public Handler(QueryFactory db) {
            _db = db;
        }

        // have to figure out a way to get a mapping of fields we care about into here
        public async Task Handle(CreateCommand request, CancellationToken cancellationToken) {
            await Console.Out.WriteLineAsync("Records have been created");
            var sfRecord = request.ChangeEvent;
            var mappedFields = await _db
                .Query("salesforce.mapped_fields")
                .Select(
                    "id as Id",
                    "schema_id as SchemaId",
                    "salesforce_field_name as SalesforceFieldName",
                    "postgres_field_name as PostgresFieldName")
                .GetAsync<MappedField>(null, null, cancellationToken);
            foreach (var field in mappedFields) {
                await Console.Out.WriteLineAsync(field.ToString());
            }
            await Console.Out.WriteLineAsync("creating record with name " + sfRecord.GetValue(1).ToString());
            var fields = sfRecord.Schema.Fields;
            for (int i = 1; i < fields.Count; i++) {
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
            }
        }

        private static DateTime ConvertEpochToDateTime(long dateTimeNumber) {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(dateTimeNumber);
            return dateTimeOffset.DateTime;
        }
    }
}
