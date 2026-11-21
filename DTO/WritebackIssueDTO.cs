namespace DTO;

/// <summary>Something that refuses Two-way, and the SQL that fixes it when it is a database prerequisite.</summary>
public record WritebackIssueDTO {
    public string Message { get; set; } = "";
    public string? Sql { get; set; }
}
