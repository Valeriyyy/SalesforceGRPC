using Application.Writeback;
using Database.Repositories.Interfaces;

namespace Application.Runtime;

/// <inheritdoc />
public sealed class WorkerStatusService : IWorkerStatusService {
    private readonly IInboundHealth _inbound;
    private readonly IWritebackHealth _writeback;
    private readonly IInboundFailureRepository _inboundFailures;
    private readonly IWritebackFailureRepository _writebackFailures;
    private readonly IIncidentRepository _incidents;
    private readonly TimeProvider _time;

    public WorkerStatusService(IInboundHealth inbound, IWritebackHealth writeback,
        IInboundFailureRepository inboundFailures, IWritebackFailureRepository writebackFailures,
        IIncidentRepository incidents, TimeProvider time) {
        _inbound = inbound;
        _writeback = writeback;
        _inboundFailures = inboundFailures;
        _writebackFailures = writebackFailures;
        _incidents = incidents;
        _time = time;
    }

    public async Task<WorkerStatus> GetAsync(CancellationToken cancellationToken = default) {
        var since = (_time.GetUtcNow() - IIncidentRepository.Retention).UtcDateTime;

        return new WorkerStatus {
            Inbound = InboundRun.From(_inbound),
            Writeback = _writeback.State is RunState.Idle ? null : new WritebackRun {
                State = _writeback.State,
                Reason = _writeback.Reason,
                LastAppliedAt = _writeback.LastAppliedAt,
                LagBytes = _writeback.LagBytes
            },
            RecentIncidents = await _incidents.ListSinceAsync(since, cancellationToken).ConfigureAwait(false),
            InboundFailures = await _inboundFailures.CountAsync(cancellationToken).ConfigureAwait(false),
            WritebackFailures = await _writebackFailures.CountAsync(cancellationToken).ConfigureAwait(false)
        };
    }
}
