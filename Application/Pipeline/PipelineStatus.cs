using Database.Models;
using Database.Repositories.Interfaces;

namespace Application.Pipeline;

/// <summary>The four Stages of the Pipeline, in the order they are set up.</summary>
public enum PipelineStage {
    OrgConnection,
    PrimaryChannel,
    TargetConnection,
    Bindings
}

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

/// <summary>
/// Why a Stage is To do or Needs attention, as a domain condition. Turning one into words is the UI's job.
/// </summary>
public enum StageReason {
    NoOrgConnection,
    OrgConnectionIncomplete,
    OrgConnectionFailed,
    NoPrimaryChannel,
    PrimaryChannelHasNoMembers,
    CheckpointNearExpiry,
    CheckpointExpired,
    NoTargetConnection,
    TargetConnectionNotProved,
    TargetConnectionFailed,
    NoActiveBindings,
    BindingForcedIncomplete
}

/// <summary>One Stage's status, what it waits on, and why.</summary>
public abstract record StageState {
    public abstract PipelineStage Stage { get; }
    public StageStatus Status { get; init; }

    /// <summary>The earlier Stages that are not OK. Non-empty only when <see cref="Status"/> is Waiting.</summary>
    public IReadOnlyList<PipelineStage> WaitingOn { get; init; } = [];

    /// <summary>Why the Stage is To do or Needs attention. Empty when it is OK or Waiting.</summary>
    public IReadOnlyList<StageReason> Reasons { get; init; } = [];
}

public sealed record OrgConnectionStage : StageState {
    public override PipelineStage Stage => PipelineStage.OrgConnection;
    public string? RunAsUsername { get; init; }
    public string? OrgUrl { get; init; }
    public string? OrgId { get; init; }
    public DateTime? LastConnectedAt { get; init; }
}

public sealed record PrimaryChannelStage : StageState {
    public override PipelineStage Stage => PipelineStage.PrimaryChannel;
    public string? ChannelFullName { get; init; }
    public int MemberCount { get; init; }
    public DateTime? CheckpointSavedAt { get; init; }
    public TimeSpan? CheckpointAge { get; init; }

    /// <summary>How long until Salesforce discards the events after the Checkpoint; zero once it has.</summary>
    public TimeSpan? CheckpointExpiresIn { get; init; }

    /// <summary>Where the worker begins while there is no Checkpoint.</summary>
    public StartingPoint? StartingPoint { get; init; }
}

public sealed record TargetConnectionStage : StageState {
    public override PipelineStage Stage => PipelineStage.TargetConnection;
    public TargetDatabaseEngine? Engine { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? DatabaseName { get; init; }
    public string? FilePath { get; init; }
    public DateTime? LastConnectedAt { get; init; }
}

public sealed record BindingsStage : StageState {
    public override PipelineStage Stage => PipelineStage.Bindings;
    public BindingStateCounts Counts { get; init; } = new();
}

/// <summary>Every Stage of the Pipeline, and which one to do next.</summary>
public sealed record PipelineStatus(
    OrgConnectionStage OrgConnection,
    PrimaryChannelStage PrimaryChannel,
    TargetConnectionStage TargetConnection,
    BindingsStage Bindings) {

    /// <summary>The four Stages in setup order.</summary>
    public IReadOnlyList<StageState> Stages => [OrgConnection, PrimaryChannel, TargetConnection, Bindings];

    /// <summary>The earliest Stage that is not OK, or null when every one is.</summary>
    public PipelineStage? NextStep => Stages.FirstOrDefault(s => s.Status != StageStatus.Ok)?.Stage;

    public bool AllOk => NextStep is null;
}
