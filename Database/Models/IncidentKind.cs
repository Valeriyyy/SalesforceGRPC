namespace Database.Models;

public enum IncidentKind {
    /// <summary>One unbroken stretch of Retrying; each retry inside it counts an occurrence.</summary>
    Retrying,

    /// <summary>Syncing was running and stopped because something the user must fix went wrong.</summary>
    Blocked,

    /// <summary>Salesforce rejected the Checkpoint, so the changes between it and the oldest kept event are lost.</summary>
    CheckpointDiscarded,

    /// <summary>The application started.</summary>
    Started,

    /// <summary>The application stopped cleanly.</summary>
    Stopped
}
