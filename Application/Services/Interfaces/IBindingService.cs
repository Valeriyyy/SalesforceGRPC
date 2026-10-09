using Application.Bindings;
using DTO;

namespace Application.Services.Interfaces;

/// <summary>
/// Everything about Bindings: which Entity lands in which Target Table, which field feeds which column, and
/// whether the worker should be applying any of it.
/// </summary>
/// <remarks>
/// One service so there is one place to look and one interface for tests to drive. It talks to the App
/// Database and the Target Database but never to Salesforce's Tooling API — Channels and Channel Members stay
/// on <see cref="IPlatformEventService"/>.
/// </remarks>
public interface IBindingService {

    #region Discovery

    /// <summary>
    /// The fields of a Channel Member's Entity that a Field Mapping may name, already flattened, each with its
    /// Salesforce Field Type and its current or suggested Target Column.
    /// </summary>
    Task<IReadOnlyList<BindableFieldDTO>> GetBindableFieldsAsync(int memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same as <see cref="GetBindableFieldsAsync"/>, for a Binding's Entity against its Target Table —
    /// whether or not any Channel Member is linked to it yet.
    /// </summary>
    Task<IReadOnlyList<BindableFieldDTO>> GetBindableFieldsForBindingAsync(int bindingId, CancellationToken cancellationToken = default);

    /// <summary>Tables in the Target Database, each marked with the Entity already bound to it.</summary>
    Task<IReadOnlyList<TargetTableDTO>> GetTargetTablesAsync(string? schemaName, CancellationToken cancellationToken = default);

    /// <summary>Columns of one Target Table, each marked with the Salesforce field mapped to it.</summary>
    Task<IReadOnlyList<TargetColumnDTO>> GetTargetColumnsAsync(string? schemaName, string tableName,
        int? bindingId = null, CancellationToken cancellationToken = default);

    /// <summary>The columns of a Binding's Target Table, each marked with the Salesforce field mapped to it.</summary>
    Task<IReadOnlyList<TargetColumnDTO>> GetBindingColumnsAsync(int bindingId, CancellationToken cancellationToken = default);

    #endregion

    #region Bindings

    Task<IReadOnlyList<BindingDTO>> GetBindingsAsync(CancellationToken cancellationToken = default);

    Task<BindingDTO> GetBindingAsync(int bindingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an Incomplete Binding for a Channel Member's Entity and links the member to it. When the Entity
    /// is already bound (through another Channel's member), links the member to that Binding and returns it
    /// instead, since an Entity has one destination.
    /// </summary>
    Task<BindingDTO> CreateBindingAsync(int memberId, CreateBindingDTO dto, CancellationToken cancellationToken = default);

    /// <summary>Replaces the Binding's Field Mapping set. Leaves the Key Mapping alone.</summary>
    Task<BindingDTO> SetFieldMappingsAsync(int bindingId, SetFieldMappingsDTO dto, CancellationToken cancellationToken = default);

    /// <summary>Names the Target Column that holds the Salesforce record ID.</summary>
    Task<BindingDTO> SetKeyMappingAsync(int bindingId, SetKeyMappingDTO dto, CancellationToken cancellationToken = default);

    Task<BindingDTO> SetSoftDeleteAsync(int bindingId, SetSoftDeleteDTO dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// The columns of the Binding's Target Table that <see cref="SetSoftDeleteAsync"/> would accept as the
    /// soft delete flag — one rule for both, so what is offered is what will be accepted.
    /// </summary>
    Task<IReadOnlyList<string>> GetSoftDeleteColumnsAsync(int bindingId, CancellationToken cancellationToken = default);

    /// <summary>Runs validation without changing the Binding's state.</summary>
    Task<BindingValidationDTO> ValidateBindingAsync(int bindingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the Binding as it would be with <paramref name="proposed"/> in place of its Field Mappings,
    /// writing nothing — so an editor can check a draft before saving it.
    /// </summary>
    /// <remarks>The stored Key Mapping and soft delete settings are kept.</remarks>
    Task<BindingValidationDTO> ValidateProposedFieldMappingsAsync(int bindingId, SetFieldMappingsDTO proposed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches a Binding on. Re-runs validation first and never trusts a previously stored result.
    /// </summary>
    Task<BindingDTO> ActivateAsync(int bindingId, CancellationToken cancellationToken = default);

    /// <summary>Switches a Binding off, keeping every Field Mapping so it can be switched back on unchanged.</summary>
    Task<BindingDTO> DeactivateAsync(int bindingId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a Binding and its Field Mappings, and unlinks any Channel Member pointing at it.</summary>
    Task DeleteBindingAsync(int bindingId, CancellationToken cancellationToken = default);

    #endregion

    #region Primary channel

    /// <summary>The Primary Channel's local ID, or null when none has been selected.</summary>
    Task<int?> GetPrimaryChannelIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Leaves the application with no Primary Channel, so nothing streams. Keeps the channel's Checkpoint, so
    /// making it Primary again can Resume, and changes no Binding.
    /// </summary>
    Task ClearPrimaryChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes one channel the Primary Channel. Rejects a channel that is not Change Data Capture.
    /// </summary>
    /// <remarks>
    /// When the channel still has a Checkpoint, <see cref="SetPrimaryChannelDTO.Start"/> decides whether the
    /// worker resumes from it (the default) or discards it and begins at Earliest or Latest, once. Resume is
    /// refused once the Checkpoint is older than the 72 hours Salesforce keeps events for.
    /// </remarks>
    Task SetPrimaryChannelAsync(SetPrimaryChannelDTO dto, CancellationToken cancellationToken = default);

    /// <summary>Whether a channel has a Checkpoint, how old it is, and its Starting Point.</summary>
    Task<ChannelStartDTO> GetChannelStartAsync(int channelId, CancellationToken cancellationToken = default);

    /// <summary>Sets where the worker begins a channel that has no Checkpoint. No effect once one exists.</summary>
    Task<ChannelStartDTO> SetStartingPointAsync(int channelId, SetStartingPointDTO dto, CancellationToken cancellationToken = default);

    /// <summary>What the worker should subscribe to and which Bindings it should apply.</summary>
    Task<SubscriptionPlan> GetSubscriptionPlanAsync(CancellationToken cancellationToken = default);

    #endregion
}
