using Newtonsoft.Json;

namespace SalesforceGrpc.Models;

public class ChangeEventBatch {
    public List<RecordChangeSet> Records { get; set; } = new();

    /// <summary>
    /// Converts all changes to SQL statements
    /// </summary>
    public IEnumerable<string> ToSqlStatements() {
        return Records.Select(r => r.ToSqlUpdateStatement()).Where(s => !string.IsNullOrEmpty(s));
    }

    /// <summary>
    /// Converts to JSON for client consumption
    /// </summary>
    // public string ToJson() {
    //     var jsonRecords = Records.Select(r => new {
    //         r.EntityName,
    //         r.RecordId,
    //         r.ChangeType,
    //         ChangedFields = r.ChangedFields.Select(f => new {
    //             f.FieldName,
    //             f.Value,
    //             f.AvroTypeName
    //         })
    //     });
    //     return JsonConvert.SerializeObject(jsonRecords, Formatting.Indented);
    // }
}