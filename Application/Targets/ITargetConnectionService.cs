using DTO;

namespace Application.Targets;

/// <summary>
/// Setting up, proving, editing and repointing the Target Connection.
/// </summary>
/// <remarks>
/// Owns the whole user-facing lifecycle. The one state change that happens elsewhere is the worker recording
/// a write failure or a recovery, because the worker is where the connection is genuinely exercised.
/// </remarks>
public interface ITargetConnectionService {
    Task<TargetConnectionDTO> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Every engine, with the fields it asks for and whether it can be used. What a form renders from.</summary>
    IReadOnlyList<EngineDefinitionDTO> GetEngines();

    /// <summary>
    /// Stores the details and proves them. Creates the Target Connection, or edits the existing one
    /// non-destructively.
    /// </summary>
    /// <remarks>
    /// Always persists. A proof that fails leaves the connection Incomplete with the error — a user whose
    /// database is briefly unreachable must still be able to record what they typed.
    /// <para>
    /// A change of identity is accepted only while no Binding exists; with any, it is a repoint. A blank
    /// password on an edit with an unchanged identity keeps the stored one.
    /// </para>
    /// </remarks>
    Task<TargetConnectionDTO> SaveAsync(SaveTargetConnectionDTO request, CancellationToken cancellationToken = default);

    /// <summary>Proves the stored Target Connection again and records the outcome. The user's repair button.</summary>
    Task<TargetConnectionDTO> VerifyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a write to the Target Database failed, moving the connection to Failed.
    /// </summary>
    /// <remarks>
    /// Called by the worker, which is where the connection is genuinely exercised. Does not raise the change
    /// signal: the worker is the thing that would be woken, and it already knows.
    /// </remarks>
    Task RecordWriteFailureAsync(Exception failure, CancellationToken cancellationToken = default);

    /// <summary>What a repoint would destroy. Call this before confirming one.</summary>
    Task<RepointPreviewDTO> PreviewRepointAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points the application at a different database, destroying every Binding.
    /// </summary>
    /// <remarks>
    /// All or nothing, unlike save: the new details are proved <em>before</em> anything is destroyed, because
    /// destroying Bindings and then discovering the new database is unreachable leaves the user with neither.
    /// </remarks>
    Task<TargetConnectionDTO> RepointAsync(RepointTargetConnectionDTO request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the Replication Credential and proves it: it logs in to the same database, may read its changes,
    /// and the server can do Two-way. Stored either way, with the outcome. A credential edit never Repoints.
    /// </summary>
    Task<TargetConnectionDTO> SetReplicationCredentialAsync(SetReplicationCredentialDTO request, CancellationToken cancellationToken = default);

    /// <summary>Proves the stored Replication Credential again and records the outcome.</summary>
    Task<TargetConnectionDTO> VerifyReplicationCredentialAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the Replication Credential; Two-way Bindings stop writing back until another is entered.</summary>
    Task<TargetConnectionDTO> ClearReplicationCredentialAsync(CancellationToken cancellationToken = default);
}
