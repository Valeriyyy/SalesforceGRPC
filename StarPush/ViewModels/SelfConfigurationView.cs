using DTO;

namespace StarPush.ViewModels;

/// <summary>What the last Bootstrap's Self-Configuration did, and what it left to do by hand.</summary>
public sealed record SelfConfigurationView(DateTime At, bool Configured, string Summary, IReadOnlyList<string> ManualSteps) {
    public static SelfConfigurationView? From(SelfConfigurationDTO? outcome) => outcome is null
        ? null
        : new(outcome.At, outcome.Configured, outcome.Summary, outcome.ManualSteps);
}
