namespace StarPush.ViewModels;

/// <summary>Where the Org Connection is in its setup, which decides what the page shows and its one primary action.</summary>
public enum OrgConnectionPhase {
    NotStarted,
    ReadyToApprove,
    NeedsConsumerSecret,
    Approved,
    Unverified,
    Connected,
    Failed
}
