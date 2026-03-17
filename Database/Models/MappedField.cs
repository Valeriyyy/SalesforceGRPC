using System.ComponentModel.DataAnnotations.Schema;
namespace Database.Models;
public class MappedField {
    public int Id { get; set; }
    /// <summary>
    /// The id of the db schema in the database, not the schema id of the avro schema.
    /// This is used to link the mapped field to the correct db schema and table.
    /// </summary>
    [Column("schema_id")]
    public int SchemaId { get; set; }
    [Column("salesforce_field_name")]
    public required string SalesforceFieldName { get; set; }
    [Column("postgres_field_name")]
    public required string PostgresFieldName { get; set; }

    // public override string ToString() {
    //     return $"{SchemaId} {SalesforceFieldName} => {PostgresFieldName}";
    // }
}
