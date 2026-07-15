using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models;
public class CDCSchema {
    public int Id { get; set; }
    [Column("avro_schema_id")]
    public int AvroSchemaId { get; set; }
    [Column("entity_name")]
    public required string EntityName { get; set; }
    [Column("schema_id")]
    public required string SchemaId { get; set; }
    [Column("schema_name")]
    public required string SchemaName { get; set; }
    [Column("db_schema_full_name")]
    public required string DbSchemaFullName { get; set; }
    [Column("soft_delete_enabled")]
    public bool SoftDeletedEnabled { get; set; }
    [Column("soft_delete_column_name")]
    public string SoftDeleteColumnName { get; set; }

    public DbAvroSchema AvroSchema { get; set; }

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