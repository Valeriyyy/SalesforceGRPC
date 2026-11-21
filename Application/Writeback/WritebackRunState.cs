namespace Application.Writeback;

public enum WritebackRunState {
    /// <summary>No Binding is Two-way and Active, so there is nothing to write back.</summary>
    Idle,

    /// <summary>A Binding wants Writeback but something it needs is missing; <see cref="IWritebackHealth.Reason"/> says what.</summary>
    Blocked,

    /// <summary>Reading the Target Database's changes and writing them back.</summary>
    Running,

    /// <summary>Paused after a failure that may pass, and about to try again.</summary>
    Retrying
}
