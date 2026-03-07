namespace SalesforceGrpc.Models;

public record RecordChangeSet {
    public string EntityName { get; set; }
    public List<string> RecordIds { get; set; } = new();
    public string ChangeType { get; set; }
    public List<ChangedField> ChangedFields { get; set; } = new();

    /// <summary>
    /// Constructor for single record ID (backward compatibility)
    /// </summary>
    public RecordChangeSet(string entityName, string recordId, string changeType) {
        EntityName = entityName;
        RecordIds.Add(recordId);
        ChangeType = changeType;
    }

    /// <summary>
    /// Constructor for multiple record IDs
    /// </summary>
    public RecordChangeSet(string entityName, List<string> recordIds, string changeType) {
        EntityName = entityName;
        RecordIds = recordIds;
        ChangeType = changeType;
    }

    public Dictionary<string, object> ToDataObject() {
        var dictionary = new Dictionary<string, object>();
        
        foreach (var field in ChangedFields) {
            dictionary[field.FieldName] = field.Value;
        }
        
        // Convert dictionary to anonymous object using ExpandoObject for dynamic property access
        // dynamic expandoObject = new System.Dynamic.ExpandoObject();
        // var expandoDictionary = (IDictionary<string, object>)expandoObject;
        var data = new Dictionary<string, object>();
        
        foreach (var kvp in dictionary) {
            data[kvp.Key] = kvp.Value;
        }
        
        return data;
    }

    /// <summary>
    /// Converts the change set to a SQL UPDATE/INSERT statement
    /// For multiple record IDs, generates a single UPDATE with WHERE IN clause
    /// </summary>
    public string ToSqlUpdateStatement() {
        if (!RecordIds.Any()) {
            return string.Empty;
        }

        if (ChangeType.Equals("UPDATE", StringComparison.OrdinalIgnoreCase)) {
            var setClauses = ChangedFields.Select(f => $"{f.FieldName} = {FormatSqlValue(f.Value, f.AvroTypeName)}");
            var setClausesStr = string.Join(", ", setClauses);
            
            if (RecordIds.Count == 1) {
                // Single record: use simple WHERE
                return "UPDATE " + EntityName + " SET " + setClausesStr + " WHERE sf_id = '" + RecordIds[0] + "';";
            } else {
                // Multiple records: use WHERE IN
                var inClause = string.Join("', '", RecordIds);
                return "UPDATE " + EntityName + " SET " + setClausesStr + " WHERE sf_id IN ('" + inClause + "');";
            }
        } else if (ChangeType.Equals("CREATE", StringComparison.OrdinalIgnoreCase)) {
            // INSERT statements must be individual - one per record
            var statements = new List<string>();
            var columns = string.Join(", ", ChangedFields.Select(f => f.FieldName));
            var values = string.Join(", ", ChangedFields.Select(f => FormatSqlValue(f.Value, f.AvroTypeName)));
            
            foreach (var recordId in RecordIds) {
                statements.Add("INSERT INTO " + EntityName + " (Id, " + columns + ") VALUES ('" + recordId + "', " + values + ");");
            }
            
            return string.Join("\n", statements);
        }
        
        return string.Empty;
    }

    private static string FormatSqlValue(object? value, string avroType) {
        if (value == null) return "NULL";
        
        return avroType switch {
            "string" => $"'{EscapeSqlString(value.ToString() ?? "")}'",
            "int" or "long" => value.ToString() ?? "",
            "double" or "float" => value.ToString() ?? "",
            "boolean" => value.ToString()?.ToUpper() == "TRUE" ? "1" : "0",
            "bytes" => $"0x{BitConverter.ToString((byte[])value).Replace("-", "")}",
            _ => $"'{EscapeSqlString(value.ToString() ?? "")}'"
        };
    }

    private static string EscapeSqlString(string value) {
        return value.Replace("'", "''");
    }
}



