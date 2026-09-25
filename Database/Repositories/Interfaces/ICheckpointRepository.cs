using Database.Models;

namespace Database.Repositories.Interfaces;

/// <summary>
/// Reads and writes each Channel's Checkpoint in the App Database.
/// </summary>
/// <remarks>
/// One worker per App Database is assumed (ADR 0005): two would overwrite each other's Checkpoint.
/// </remarks>
public interface ICheckpointRepository {
    /// <summary>The Channel's Checkpoint, or null when it has none.</summary>
    Task<Checkpoint?> GetAsync(int channelId, CancellationToken cancellationToken = default);

    /// <summary>Saves the Channel's Checkpoint, replacing any earlier one, stamped with the current time.</summary>
    Task SaveAsync(int channelId, byte[] replayId, CancellationToken cancellationToken = default);

    /// <summary>Discards the Channel's Checkpoint, if it has one.</summary>
    Task DeleteAsync(int channelId, CancellationToken cancellationToken = default);
}
