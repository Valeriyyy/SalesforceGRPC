using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// The open Writeback Failures: Target Table changes Salesforce refused, one per row of a Binding.
/// </summary>
public interface IWritebackFailureRepository {
    /// <summary>
    /// Records a failure, replacing any open one for the same row. The replacement keeps the first failure's
    /// time and counts the attempt, so the user sees how long the row has been stuck.
    /// </summary>
    Task RecordAsync(WritebackFailure failure, CancellationToken cancellationToken = default);

    /// <summary>Clears a row's open failure, after a Writeback of that row succeeded.</summary>
    Task ClearAsync(int bindingId, string rowKey, CancellationToken cancellationToken = default);

    Task<List<WritebackFailure>> ListAsync(int? bindingId = null, CancellationToken cancellationToken = default);

    Task<WritebackFailure?> GetAsync(int failureId, CancellationToken cancellationToken = default);

    /// <summary>Removes a failure: the user dismissed it, or the row it named is gone.</summary>
    Task DeleteAsync(int failureId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
