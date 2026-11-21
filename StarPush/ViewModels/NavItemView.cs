using Application.Pipeline;

namespace StarPush.ViewModels;

/// <summary>One sidebar entry. Overview has no step number and no status.</summary>
public sealed record NavItemView(NavKey Key, string Label, string Href, int? StepNumber, StageStatus? Status);
