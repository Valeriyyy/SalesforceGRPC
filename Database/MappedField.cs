using System.ComponentModel.DataAnnotations.Schema;
namespace Database;
public class MappedField {
    public int Id { get; set; }
    [Column("schema_id")]
    public string SchemaId { get; set; }
    [Column("salesforce_field_name")]
    public string SalesforceFieldName { get; set; }
    [Column("psotgres_field_name")]
    public string PostgresFieldName { get; set; }

    public override string ToString() {
        return $"{SchemaId} {SalesforceFieldName} => {PostgresFieldName}";
    }
}
