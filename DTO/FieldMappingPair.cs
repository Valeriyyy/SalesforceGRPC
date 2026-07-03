namespace DTO;

public record FieldMappingPair {
    public string SFField { get; set; }
    public string DBField { get; set; }
}