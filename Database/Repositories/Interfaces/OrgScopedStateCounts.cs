namespace Database.Repositories.Interfaces;

/// <summary>
/// Counts of the org-specific state Disconnect destroys, so the user can be told what they are about to lose
/// before they lose it.
/// </summary>
public sealed record OrgScopedStateCounts {
    public int Bindings { get; init; }
    public int FieldMappings { get; init; }
    public int AvroSchemas { get; init; }
    public int Channels { get; init; }
    public int ChannelMembers { get; init; }

    /// <summary>
    /// The Target Connection that will go too, described as engine and address, or null when none is
    /// configured. Named in the preview so its loss is disclosed rather than discovered; see docs/adr/0004.
    /// </summary>
    public string? TargetConnection { get; init; }
}
