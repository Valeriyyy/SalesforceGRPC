using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// Everything the editor shows. <see cref="Target"/> is what needs the Target Database, and is null with
/// <see cref="TargetError"/> set when it could not be read; the header, state and Delete work regardless.
/// </summary>
/// <param name="MemberId">
/// The Primary Channel's member for this Entity, where deleting and binding again starts. Null when the Entity
/// is not on the Primary Channel.
/// </param>
public sealed record BindingEditorView(
    int Id,
    string Entity,
    string TargetTable,
    BindingRowState State,
    int? MemberId,
    int FieldMappingCount,
    string? KeyMappingColumn,
    bool SoftDeleteEnabled,
    string? SoftDeleteColumnName,
    BindingTargetView? Target,
    TargetDatabaseErrorView? TargetError,
    string SyncDirection,
    WritebackSetupDTO? Writeback,
    string? WritebackError,
    IReadOnlyList<WritebackFailureDTO> WritebackFailures) {

    public static BindingEditorView For(BindingDTO binding, int? memberId, BindingTargetView? target,
        TargetDatabaseErrorView? targetError, WritebackSetupDTO? writeback = null, string? writebackError = null,
        IReadOnlyList<WritebackFailureDTO>? writebackFailures = null) => new(
        binding.Id,
        binding.EntityName,
        binding.TargetTable,
        BindingsView.StateOf(binding),
        memberId,
        binding.FieldMappingCount,
        binding.KeyMappingColumn,
        binding.SoftDeleteEnabled,
        binding.SoftDeleteColumnName,
        target,
        targetError,
        binding.SyncDirection,
        writeback,
        writebackError,
        writebackFailures ?? []);
}
