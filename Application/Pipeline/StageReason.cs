namespace Application.Pipeline;

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
    BindingForcedIncomplete,
    WritebackFailuresOpen,
    WritebackLagging,
    WritebackBlocked
}
