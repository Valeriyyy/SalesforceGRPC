using Database.Models;
using DTO;

namespace StarPush.ViewModels;

/// <summary>
/// The Bindings list: every Entity on the Primary Channel and where it lands. Read-only — every row leads to the
/// page where it is changed.
/// </summary>
/// <remarks>
/// Rows are the Primary Channel's members, matched to Bindings by Entity rather than by the member's own link,
/// since an Entity bound through another Channel's member is still bound. Entities on other Channels are left
/// out: they are not what the worker streams.
/// </remarks>
public sealed record BindingsView(
    IReadOnlyList<SetupStepView> Waiting,
    string? PrimaryChannelLabel,
    IReadOnlyList<BindingRowView> Rows) {

    public static BindingsView WaitingOn(IReadOnlyList<SetupStepView> missing) => new(missing, null, []);

    public static BindingsView For(PlatformEventChannelEntity? primary, IReadOnlyList<BindingDTO> bindings,
        IReadOnlyList<WritebackFailureDTO>? writebackFailures = null) {
        if (primary is null) {
            return new BindingsView([], null, []);
        }

        var byEntity = BindingsByEntity(bindings);
        var failures = (writebackFailures ?? []).GroupBy(f => f.BindingId).ToDictionary(g => g.Key, g => g.Count());

        return new BindingsView([], primary.MasterLabel ?? primary.DeveloperName, primary.Members
            .OrderBy(m => m.SelectedEntity, StringComparer.OrdinalIgnoreCase)
            .Select(m => byEntity.TryGetValue(m.SelectedEntity, out var b)
                ? new BindingRowView(m.Id, m.SelectedEntity, b.Id, b.TargetTable, b.KeyMappingColumn,
                    b.FieldMappingCount, b.FieldCount, StateOf(b), $"/bindings/{b.Id}",
                    b.SyncDirection == nameof(SyncDirection.TwoWay), failures.GetValueOrDefault(b.Id))
                : new BindingRowView(m.Id, m.SelectedEntity, null, null, null, 0, null, BindingRowState.Unbound,
                    $"/bindings/new?member={m.Id}", false, 0))
            .ToList());
    }

    internal static Dictionary<string, BindingDTO> BindingsByEntity(IEnumerable<BindingDTO> bindings) =>
        bindings
            .GroupBy(b => b.EntityName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    internal static BindingRowState StateOf(BindingDTO binding) => binding.State switch {
        nameof(BindingState.Active) => BindingRowState.Active,
        nameof(BindingState.Inactive) => BindingRowState.Inactive,
        _ when binding.NeedsAttention => BindingRowState.NeedsAttention,
        _ => BindingRowState.Incomplete
    };
}
