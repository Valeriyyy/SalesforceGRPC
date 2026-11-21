namespace DTO;

/// <summary>The Channel a <see cref="NewChannelDTO"/> produced.</summary>
public record NewChannelResultDTO {
    public int ChannelId { get; set; }

    /// <summary>
    /// True when Salesforce already had a Channel with this Full Name, so it was adopted rather than created.
    /// </summary>
    public bool Adopted { get; set; }

    /// <summary>How many Channel Members it carries — always 0 for a Channel that was created.</summary>
    public int MemberCount { get; set; }

    public string FullName { get; set; } = string.Empty;
}
