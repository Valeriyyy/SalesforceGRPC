namespace DTO;

public record CreateFieldMappingDTO {
    public int SchemaId { get; set; }
    List<FieldMappingPair> FieldMappingPairs { get; set; }
}