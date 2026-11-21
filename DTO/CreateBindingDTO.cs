namespace DTO;

/// <summary>
/// Creates a Binding for a Channel Member's Entity, pointing it at an existing Target Table.
/// </summary>
/// <remarks>
/// Schema and table are separate rather than one qualified string because the Target Database metadata
/// queries take them separately, and splitting a user-supplied "a.b.c" is guesswork.
/// </remarks>
public record CreateBindingDTO {
    /// <summary>The schema containing the Target Table, e.g. "salesforce", or null for an engine with no schema concept (e.g. SQLite).</summary>
    public string? TargetSchema { get; set; }

    /// <summary>The Target Table name, e.g. "account".</summary>
    public string TargetTable { get; set; } = "";
}
