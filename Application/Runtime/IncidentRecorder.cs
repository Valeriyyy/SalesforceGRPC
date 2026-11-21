using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Runtime;

/// <inheritdoc />
/// <remarks>
/// <para>
/// An Incident is one unbroken stretch of trouble. Entering Retrying opens one, and every further retry counts an
/// occurrence. Getting back to Running closes it, but a Retrying that follows within <see cref="MergeWindow"/>
/// reopens the same one: a stream that opens and drops again straight away has not recovered, and a dozen
/// one-retry Incidents would bury everything else.
/// </para>
/// <para>
/// Running into Blocked is an Incident, since a working Pipeline broke; so is a Retrying that turns out to need
/// the user, once the direction has run at all. Blocked before it ever ran, or after Idle, is not: that is a
/// Pipeline not set up yet, and the Stage cards already say so. Idle is never an Incident.
/// </para>
/// <para>
/// Which Incident is open is kept in memory per direction; a restart closes whatever was left open.
/// </para>
/// </remarks>
public sealed class IncidentRecorder : IIncidentRecorder {
    /// <summary>How soon after recovering a new Retrying still counts as the same stretch of trouble.</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(5);

    private readonly IIncidentRepository _incidents;
    private readonly TimeProvider _time;
    private readonly ILogger<IncidentRecorder> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<RunDirection, Track> _tracks = new();

    private sealed class Track {
        public RunState? Last;

        /// <summary>Whether this direction has been Running since the application started.</summary>
        public bool HasRun;
        public (int Id, IncidentKind Kind)? Open;
        public (int Id, DateTimeOffset EndedAt)? Recovered;
    }

    public IncidentRecorder(IIncidentRepository incidents, TimeProvider time, ILogger<IncidentRecorder> logger) {
        _incidents = incidents;
        _time = time;
        _logger = logger;
    }

    public Task ReportAsync(RunDirection direction, RunState state, string? reason,
        CancellationToken cancellationToken = default) =>
        Guarded(async () => {
            if (!_tracks.TryGetValue(direction, out var track)) {
                _tracks[direction] = track = new Track();
            }

            var previous = track.Last;
            track.Last = state;
            var now = _time.GetUtcNow();

            switch (state) {
                case RunState.Retrying:
                    await Retrying(direction, track, reason ?? "Retrying after a failure.", now, cancellationToken).ConfigureAwait(false);
                    break;
                case RunState.Blocked when previous == RunState.Running
                                           || (track.HasRun && track.Open is { Kind: IncidentKind.Retrying }):
                    await Close(track, now, cancellationToken).ConfigureAwait(false);
                    track.Open = (await Open(direction, IncidentKind.Blocked, reason ?? "Stopped by a problem that needs you.",
                        now, null, cancellationToken).ConfigureAwait(false), IncidentKind.Blocked);
                    break;
                case RunState.Blocked:
                    // Never running yet: a Pipeline not set up, which the Stage cards already say. A failed attempt
                    // before it ends here, as the stretch of trouble it was.
                    await Close(track, now, cancellationToken).ConfigureAwait(false);
                    break;
                case RunState.Running:
                    track.HasRun = true;
                    await Close(track, now, cancellationToken).ConfigureAwait(false);
                    break;
                case RunState.Idle:
                    await Close(track, now, cancellationToken).ConfigureAwait(false);
                    track.Recovered = null;
                    break;
            }
        }, "a Run State change");

    private async Task Retrying(RunDirection direction, Track track, string message, DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (track.Open is { Kind: IncidentKind.Retrying } open) {
            await _incidents.ContinueAsync(open.Id, message, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (track.Open is not null) {
            await Close(track, now, cancellationToken).ConfigureAwait(false);
        }

        if (track.Recovered is { } recovered && now - recovered.EndedAt <= MergeWindow) {
            await _incidents.ContinueAsync(recovered.Id, message, cancellationToken).ConfigureAwait(false);
            track.Open = (recovered.Id, IncidentKind.Retrying);
        } else {
            track.Open = (await Open(direction, IncidentKind.Retrying, message, now, null, cancellationToken).ConfigureAwait(false),
                IncidentKind.Retrying);
        }
        track.Recovered = null;
    }

    private async Task Close(Track track, DateTimeOffset now, CancellationToken cancellationToken) {
        if (track.Open is not { } open) {
            return;
        }

        // Cleared first: a failed close must not leave the recorder continuing an Incident that may have ended.
        track.Open = null;
        await _incidents.CloseAsync(open.Id, now.UtcDateTime, cancellationToken).ConfigureAwait(false);
        track.Recovered = open.Kind == IncidentKind.Retrying ? (open.Id, now) : null;
    }

    private Task<int> Open(RunDirection? direction, IncidentKind kind, string message, DateTimeOffset now,
        DateTimeOffset? endedAt, CancellationToken cancellationToken) =>
        _incidents.OpenAsync(new Incident {
            Direction = direction,
            Kind = kind,
            Message = message,
            StartedAt = now.UtcDateTime,
            EndedAt = endedAt?.UtcDateTime
        }, cancellationToken);

    public Task CheckpointDiscardedAsync(string message, CancellationToken cancellationToken = default) =>
        Guarded(async () => {
            var now = _time.GetUtcNow();
            await Open(RunDirection.Inbound, IncidentKind.CheckpointDiscarded, message, now, now, cancellationToken)
                .ConfigureAwait(false);
        }, "a discarded Checkpoint");

    public Task StartedAsync(CancellationToken cancellationToken = default) =>
        Guarded(async () => {
            var now = _time.GetUtcNow();
            var previous = await _incidents.GetLatestLifecycleKindAsync(cancellationToken).ConfigureAwait(false);
            // Whatever the previous run left open ended with it; when exactly is unknown, so it ends now.
            await _incidents.CloseAllOpenAsync(now.UtcDateTime, cancellationToken).ConfigureAwait(false);
            var message = previous == IncidentKind.Started
                ? "The application started. The previous run ended unexpectedly, without stopping cleanly."
                : "The application started.";
            await Open(null, IncidentKind.Started, message, now, now, cancellationToken).ConfigureAwait(false);
        }, "the application starting");

    public Task StoppedAsync(CancellationToken cancellationToken = default) =>
        Guarded(async () => {
            var now = _time.GetUtcNow();
            await _incidents.CloseAllOpenAsync(now.UtcDateTime, cancellationToken).ConfigureAwait(false);
            _tracks.Clear();
            await Open(null, IncidentKind.Stopped, "The application stopped.", now, now, cancellationToken).ConfigureAwait(false);
        }, "the application stopping");

    /// <summary>
    /// Runs one change at a time, and swallows every failure: losing an Incident is better than losing the worker.
    /// </summary>
    private async Task Guarded(Func<Task> change, string what) {
        // Taken without the caller's token, so a stopping host still waits its turn rather than skipping the lock.
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            await change().ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Could not record an Incident for {What}; syncing carries on", what);
        } finally {
            _gate.Release();
        }
    }
}
