namespace Application.Pipeline;

/// <summary>Where one Stage stands. See Stage Status in CONTEXT.md.</summary>
public enum StageStatus {
    /// <summary>Not set up, and cannot be until an earlier Stage is OK.</summary>
    Waiting,

    /// <summary>Can be set up now, and has not been.</summary>
    ToDo,

    /// <summary>Was set up, and something about it is now wrong.</summary>
    NeedsAttention,

    Ok
}
