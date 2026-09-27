using Database.Models;
using Database.Repositories.Interfaces;

namespace Application.Pipeline;

/// <summary>
/// Answers what each Stage of the Pipeline's status is, and why, as domain data.
/// </summary>
/// <remarks>
/// The one place the Stage Status rules live, so the shell, the Overview and any later API agree on them. Reads
/// the repositories directly rather than the cached providers: a page load has to show current state.
/// </remarks>
public interface IPipelineStatusService {
    Task<PipelineStatus> GetAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PipelineStatusService : IPipelineStatusService {
    /// <summary>A Checkpoint older than this is close enough to Salesforce's retention to warn about.</summary>
    public static readonly TimeSpan CheckpointWarningAge = TimeSpan.FromHours(48);

    private readonly IOrgConnectionRepository _org;
    private readonly IPlatformEventChannelRepository _channels;
    private readonly ICheckpointRepository _checkpoints;
    private readonly ITargetConnectionRepository _target;
    private readonly IMetaRepository _meta;
    private readonly TimeProvider _time;

    public PipelineStatusService(IOrgConnectionRepository org, IPlatformEventChannelRepository channels,
        ICheckpointRepository checkpoints, ITargetConnectionRepository target, IMetaRepository meta, TimeProvider time) {
        _org = org;
        _channels = channels;
        _checkpoints = checkpoints;
        _target = target;
        _meta = meta;
        _time = time;
    }

    public async Task<PipelineStatus> GetAsync(CancellationToken cancellationToken = default) {
        var org = OrgConnection(await _org.GetAsync(cancellationToken).ConfigureAwait(false));

        var channel = await _channels.GetPrimaryChannelAsync(cancellationToken).ConfigureAwait(false);
        var checkpoint = channel is null ? null : await _checkpoints.GetAsync(channel.Id, cancellationToken).ConfigureAwait(false);
        var primaryChannel = WaitFor(PrimaryChannel(channel, checkpoint), org);

        var targetConnection = WaitFor(TargetConnection(await _target.GetAsync(cancellationToken).ConfigureAwait(false)), org);

        var counts = await _meta.CountBindingsByStateAsync(cancellationToken).ConfigureAwait(false);
        var bindings = WaitFor(Bindings(counts), primaryChannel, targetConnection);

        return new PipelineStatus(org, primaryChannel, targetConnection, bindings);
    }

    /// <summary>
    /// Makes a Stage that is still To do wait on the earlier Stages that are not OK.
    /// </summary>
    /// <remarks>
    /// Only a Stage not yet set up waits. One already set up keeps its own status whatever happens upstream, so
    /// a Failed Org Connection never hides an ageing Checkpoint or a Binding the worker forced back.
    /// </remarks>
    private static T WaitFor<T>(T stage, params StageState[] prerequisites) where T : StageState {
        var notOk = prerequisites.Where(p => p.Status != StageStatus.Ok).Select(p => p.Stage).ToList();
        if (stage.Status != StageStatus.ToDo || notOk.Count == 0) {
            return stage;
        }

        return stage with { Status = StageStatus.Waiting, WaitingOn = notOk, Reasons = [] };
    }

    private static StageStatus StatusFrom(IReadOnlyList<StageReason> reasons, params StageReason[] needAttention) =>
        reasons.Count == 0 ? StageStatus.Ok
        : reasons.Any(needAttention.Contains) ? StageStatus.NeedsAttention
        : StageStatus.ToDo;

    private static OrgConnectionStage OrgConnection(OrgConnection? connection) {
        StageReason[] reasons = connection?.ConnectionState switch {
            null => [StageReason.NoOrgConnection],
            ConnectionState.Incomplete => [StageReason.OrgConnectionIncomplete],
            ConnectionState.Failed => [StageReason.OrgConnectionFailed],
            _ => []
        };

        return new OrgConnectionStage {
            Status = StatusFrom(reasons, StageReason.OrgConnectionFailed),
            Reasons = reasons,
            RunAsUsername = connection?.RunAsUsername,
            OrgUrl = connection?.OrgUrl,
            OrgId = connection?.OrgId,
            LastConnectedAt = connection?.LastConnectedAt
        };
    }

    private PrimaryChannelStage PrimaryChannel(PlatformEventChannelEntity? channel, Checkpoint? checkpoint) {
        if (channel is null) {
            return new PrimaryChannelStage { Status = StageStatus.ToDo, Reasons = [StageReason.NoPrimaryChannel] };
        }

        var reasons = new List<StageReason>();
        if (channel.Members.Count == 0) {
            reasons.Add(StageReason.PrimaryChannelHasNoMembers);
        }

        TimeSpan? age = null;
        TimeSpan? expiresIn = null;
        if (checkpoint is not null) {
            var now = _time.GetUtcNow();
            age = now - new DateTimeOffset(DateTime.SpecifyKind(checkpoint.SavedAt, DateTimeKind.Utc));
            expiresIn = age >= Checkpoint.Retention ? TimeSpan.Zero : Checkpoint.Retention - age;

            if (checkpoint.IsExpired(now)) {
                reasons.Add(StageReason.CheckpointExpired);
            } else if (age > CheckpointWarningAge) {
                reasons.Add(StageReason.CheckpointNearExpiry);
            }
        }

        return new PrimaryChannelStage {
            Status = reasons.Count == 0 ? StageStatus.Ok : StageStatus.NeedsAttention,
            Reasons = reasons,
            ChannelFullName = channel.FullName,
            MemberCount = channel.Members.Count,
            CheckpointSavedAt = checkpoint?.SavedAt,
            CheckpointAge = age,
            CheckpointExpiresIn = expiresIn,
            StartingPoint = checkpoint is null ? channel.RestartFrom ?? channel.StartingPoint : null
        };
    }

    private static TargetConnectionStage TargetConnection(TargetConnection? connection) {
        StageReason[] reasons = connection?.ConnectionState switch {
            null => [StageReason.NoTargetConnection],
            ConnectionState.Incomplete => [StageReason.TargetConnectionNotProved],
            ConnectionState.Failed => [StageReason.TargetConnectionFailed],
            _ => []
        };

        return new TargetConnectionStage {
            Status = StatusFrom(reasons, StageReason.TargetConnectionFailed),
            Reasons = reasons,
            Engine = connection?.Engine,
            Host = connection?.Host,
            Port = connection?.Port,
            DatabaseName = connection?.DatabaseName,
            FilePath = connection?.FilePath,
            LastConnectedAt = connection?.LastConnectedAt
        };
    }

    private static BindingsStage Bindings(BindingStateCounts counts) {
        var reasons = new List<StageReason>();
        if (counts.ForcedIncomplete > 0) {
            reasons.Add(StageReason.BindingForcedIncomplete);
        }
        if (counts.Active == 0) {
            reasons.Add(StageReason.NoActiveBindings);
        }

        return new BindingsStage {
            Status = StatusFrom(reasons, StageReason.BindingForcedIncomplete),
            Reasons = reasons,
            Counts = counts
        };
    }
}
