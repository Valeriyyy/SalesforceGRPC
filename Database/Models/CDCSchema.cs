using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models;

/// <summary>
/// A Binding: the decision that one Entity's change events land in one Target Table.
/// </summary>
/// <remarks>
/// Stored as a row in salesforce.cdc_schemas. The table name predates the term and is misleading — a Binding
/// holds no schema. The Avro Schema it was last seen with hangs off <see cref="AvroSchema"/>, and rotates
/// every time Salesforce changes the Source Object's shape.
/// </remarks>
public class CDCSchema {
    public int Id { get; set; }

    [Column("avro_schema_id")]
    public int AvroSchemaId { get; set; }

    /// <summary>The Entity this Binding is for, e.g. "AccountChangeEvent".</summary>
    [Column("entity_name")]
    public required string EntityName { get; set; }

    /// <summary>The schema-qualified Target Table, e.g. "salesforce.account".</summary>
    [Column("db_schema_full_name")]
    public required string DbSchemaFullName { get; set; }

    [Column("binding_state")]
    public BindingState BindingState { get; set; } = BindingState.Incomplete;

    [Column("soft_delete_enabled")]
    public bool SoftDeleteEnabled { get; set; }

    [Column("soft_delete_column_name")]
    public string? SoftDeleteColumnName { get; set; }

    public DbAvroSchema? AvroSchema { get; set; }

    /// <summary>
    /// The Salesforce Schema Id of the Avro Schema this Binding was last seen with.
    /// </summary>
    /// <remarks>
    /// Read from the linked Avro Schema rather than stored on the Binding: Salesforce issues a new Schema Id
    /// on every revision, so a copy here would go stale. It was previously a stored column that no query ever
    /// selected, which left it silently null.
    /// </remarks>
    [NotMapped]
    public string? SchemaId => AvroSchema?.SchemaId;

    /// <summary>The Avro record name, e.g. "AccountChangeEvent". Read from the linked Avro Schema.</summary>
    [NotMapped]
    public string? SchemaName => AvroSchema?.RecordName;

    /// <summary>True when the worker should apply this Binding's events.</summary>
    [NotMapped]
    public bool IsActive => BindingState is BindingState.Active;

    public override string ToString() => $"{Id} {EntityName} -> {DbSchemaFullName} ({BindingState})";
}
