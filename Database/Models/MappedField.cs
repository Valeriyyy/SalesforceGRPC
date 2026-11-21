using System.ComponentModel.DataAnnotations.Schema;
namespace Database.Models;
public class MappedField {
    public int Id { get; set; }
    [Column("schema_id")]
    public int SchemaId { get; set; }
    [Column("salesforce_field_name")]
    public required string SalesforceFieldName { get; set; }
    [Column("target_field_name")]
    public required string TargetFieldName { get; set; }

    /// <summary>
    /// The user's Field Direction. Only read while the Binding is Two-way, and kept while it is Inbound.
    /// </summary>
    [Column("field_direction")]
    public FieldDirection FieldDirection { get; set; } = FieldDirection.TwoWay;

    // public override string ToString() {
    //     return $"{SchemaId} {SalesforceFieldName} => {PostgresFieldName}";
    // }
}
