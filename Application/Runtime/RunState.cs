namespace Application.Runtime;

/// <summary>
/// What one direction of syncing is doing right now (see Run State in CONTEXT.md). Inbound and Writeback each
/// have their own.
/// </summary>
public enum RunState {
    /// <summary>Nothing to sync, by configuration: no Primary Channel, no Active Binding, no Two-way Binding.</summary>
    Idle,

    /// <summary>Something the user must fix stops it; the reason says what.</summary>
    Blocked,

    /// <summary>Moving: streaming change events, or reading the Target Database's changes and writing them back.</summary>
    Running,

    /// <summary>Paused after a failure that may pass, and about to try again on its own.</summary>
    Retrying
}
