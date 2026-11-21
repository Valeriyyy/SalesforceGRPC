using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>The Inbound Failures waiting for the user: change events that were skipped because they could not be applied.</summary>
public interface IInboundFailureRepository {
    /// <summary>
    /// Records a failure. One that already exists for the same event keeps its first failure time, takes the
    /// latest error and counts the attempt.
    /// </summary>
    Task RecordAsync(InboundFailure failure, CancellationToken cancellationToken = default);

    /// <summary>Every failure, or one Binding's, latest first.</summary>
    Task<List<InboundFailure>> ListAsync(int? bindingId = null, CancellationToken cancellationToken = default);

    /// <summary>Removes a failure the user has dealt with.</summary>
    Task DeleteAsync(int failureId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>How many failures each Binding has; a Binding without any is left out.</summary>
    Task<Dictionary<int, int>> CountByBindingAsync(CancellationToken cancellationToken = default);
}
