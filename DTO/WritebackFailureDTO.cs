namespace DTO;

/// <summary>A Target Table change Salesforce refused.</summary>
public record WritebackFailureDTO {
    public int Id { get; set; }
    public int BindingId { get; set; }
    public string? WritebackKeyValue { get; set; }
    public string? SalesforceId { get; set; }
    public string Operation { get; set; } = "";
    public string StatusCodes { get; set; } = "";
    public string Message { get; set; } = "";
    public string ValuesSent { get; set; } = "{}";
    public DateTime FirstFailedAt { get; set; }
    public DateTime LastFailedAt { get; set; }
    public int Attempts { get; set; }
}
