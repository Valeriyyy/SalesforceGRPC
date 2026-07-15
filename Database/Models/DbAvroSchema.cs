using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Models;

public class DbAvroSchema {
    [Column("id")]
    public int Id { get; set; }
    
    [Column("schema_id")]
    public string SchemaId { get; set; }
    
    [Column("record_name")]
    public string RecordName { get; set; }
    
    [Column("schema_json")]
    public string SchemaJson { get; set; }
    
    [Column("date_created")]
    public DateTime DateCreated { get; set; }
    
    [Column("date_updated")]
    public DateTime? DateUpdated { get; set; }
}