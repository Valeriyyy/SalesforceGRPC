namespace Application.Connections;

/// <summary>
/// What Self-Configuration did, or why it did not run.
/// </summary>
public sealed record SelfConfigurationResult {
    /// <summary>True when the org was configured. False is not necessarily an error — see <see cref="ManualSteps"/>.</summary>
    public required bool Configured { get; init; }

    /// <summary>What happened, in one line, for the setup screen and the log.</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// The steps the user must perform in Setup themselves, when Self-Configuration could not.
    /// </summary>
    /// <remarks>
    /// Empty when everything was configured. Populated rather than left to documentation because a user who
    /// reaches this point is mid-setup and needs the list in front of them.
    /// </remarks>
    public IReadOnlyList<string> ManualSteps { get; init; } = [];
}
