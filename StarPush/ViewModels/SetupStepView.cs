namespace StarPush.ViewModels;

/// <summary>
/// One earlier setup step the Bindings pages wait on, with where to finish it.
/// </summary>
public sealed record SetupStepView(string Title, string Problem, string ActionLabel, string Href);
