namespace DTO;

/// <summary>
/// What Self-Configuration did during the last Bootstrap, kept so the steps left to do by hand are in front of
/// the user rather than only in the log.
/// </summary>
public record SelfConfigurationDTO {
    public DateTime At { get; set; }

    /// <summary>True when the org was configured. False means <see cref="ManualSteps"/> are left to do.</summary>
    public bool Configured { get; set; }

    public string Summary { get; set; } = "";

    public List<string> ManualSteps { get; set; } = [];
}
