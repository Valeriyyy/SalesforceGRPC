namespace StarPush.ViewModels;

/// <summary>A Run State as the card shows it: the four Run States, plus the moment after startup before the first.</summary>
public enum RunStateView {
    Starting,
    Idle,
    Blocked,
    Running,
    Retrying
}
