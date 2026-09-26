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

    /// <summary>
    /// Saves the Channel's Checkpoint, replacing any earlier one, stamped with the current time. Clears any
    /// one-time restart position, which a Checkpoint supersedes.
    /// </summary>
    Task SaveAsync(int channelId, byte[] replayId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the Channel's Checkpoint and records where the next subscription starts instead, once.
    /// </summary>
    /// <remarks>
    /// Recorded rather than left to the caller, so a restart between discarding and the next save still
    /// starts where it was asked to, not at the Channel's Starting Point. The Starting Point itself is left
    /// alone.
    /// </remarks>
    Task DiscardAsync(int channelId, StartingPoint restartFrom, CancellationToken cancellationToken = default);
}
