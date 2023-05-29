using System.ComponentModel.DataAnnotations.Schema;

namespace SalesforceGrpc; 
public class CDCSchema {
    public int Id { get; set; }
    [Column("schema_id")]
    public string SchemaId { get; set; } = null!;
    [Column("schema_name")]
    public string SchemaName { get; set; } = null!;

    public override string ToString() {
        return $"{Id} {SchemaId} {SchemaName}";
    }
}

/*var connection = new NpgsqlConnection(config.GetConnectionString("postgresLocal"));
var compiler = new PostgresCompiler();
var db = new QueryFactory(connection, compiler);

var schemaDict = (await db.Query("salesforce.cdc_schemas").Select("id as Id", "schema_id as SchemaId", "schema_name as SchemaName").GetAsync<CDCSchema>()).ToDictionary(p => p.SchemaId, p => p.SchemaName);
foreach(var kv in schemaDict) {
    Console.WriteLine(kv.Key + " " + kv.Value);
}*/