namespace StarPush.ViewModels;

/// <summary>
/// Where the Target Connection is. NotStarted shows the setup form; every other phase shows the summary.
/// </summary>
public enum TargetConnectionPhase {
    NotStarted,
    Connected,

    /// <summary>Stored, never proved: what was typed has not worked yet.</summary>
    Incomplete,

    /// <summary>Worked once and has since stopped.</summary>
    Failed
}
