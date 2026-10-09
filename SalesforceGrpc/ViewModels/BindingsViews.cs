using Application.Pipeline;
using Application.Targets;
using Database.Models;
using DTO;
using System.ComponentModel.DataAnnotations;

namespace SalesforceGrpc.ViewModels;

/// <summary>
/// One earlier setup step the Bindings pages wait on, with where to finish it.
/// </summary>
public sealed record SetupStepView(string Title, string Problem, string ActionLabel, string Href);

/// <summary>
/// What the Bindings pages need before they can do anything: a Primary Channel to read Entities from, and a
/// Target Connection whose tables can be read.
/// </summary>
/// <remarks>
/// Worked out from the Pipeline status the shell is built from, so the page and the sidebar agree. A Target
/// Connection that is set up but Failed is not missing: the list still works without it, and the editor says
/// the Target Database is unreachable in place of its mappings.
/// </remarks>
public static class BindingsSetup {
    public static IReadOnlyList<SetupStepView> Missing(PipelineStatus status) {
        var steps = new List<SetupStepView>();

        if (status.PrimaryChannel.ChannelFullName is null) {
            if (status.PrimaryChannel.WaitingOn.Contains(PipelineStage.OrgConnection)) {
                steps.Add(new SetupStepView(
                    StagePages.Title(PipelineStage.OrgConnection),
                    "Channels are read from Salesforce, so the Org Connection has to work before a Primary Channel can be chosen.",
                    "Set up Org Connection",
                    StagePages.Href(PipelineStage.OrgConnection)));
            }
            steps.Add(new SetupStepView(
                StagePages.Title(PipelineStage.PrimaryChannel),
                "No Primary Channel is chosen, so there are no Entities to bind.",
                "Choose Primary Channel",
                StagePages.Href(PipelineStage.PrimaryChannel)));
        }

        if (status.TargetConnection.Status is StageStatus.ToDo or StageStatus.Waiting) {
            steps.Add(new SetupStepView(
                StagePages.Title(PipelineStage.TargetConnection),
                status.TargetConnection.Reasons.Contains(StageReason.TargetConnectionNotProved)
                    ? "The Target Connection has never been proved, so its tables cannot be read."
                    : "There is no Target Connection yet, so there are no Target Tables to bind to.",
                "Set up Target Connection",
                StagePages.Href(PipelineStage.TargetConnection)));
        }

        return steps;
    }

    /// <summary>
    /// What to show when the Target Database could not be read for a page. A refusal of this application's own
    /// (a dropped table, no Avro Schema) is its message alone; a driver failure keeps the driver's words.
    /// </summary>
    public static TargetDatabaseErrorView LoadError(Exception ex) => ex switch {
        ValidationException or KeyNotFoundException or NoTargetDatabaseException => new(ex.Message, "", null),
        _ => new("The Target Database could not be read, so its tables and columns cannot be shown.", ex.Message, null)
    };
}

/// <summary>
/// Where a Binding stands, as the Bindings pages show it. <see cref="NeedsAttention"/> is an Incomplete Binding
/// that was Active and was forced back — by the worker or by an edit — set apart from one never finished.
/// </summary>
public enum BindingRowState {
    Unbound,
    Incomplete,
    NeedsAttention,
    Active,
    Inactive
}

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

    public static BindingsView For(PlatformEventChannelEntity? primary, IReadOnlyList<BindingDTO> bindings) {
        if (primary is null) {
            return new BindingsView([], null, []);
        }

        var byEntity = BindingsByEntity(bindings);

        return new BindingsView([], primary.MasterLabel ?? primary.DeveloperName, primary.Members
            .OrderBy(m => m.SelectedEntity, StringComparer.OrdinalIgnoreCase)
            .Select(m => byEntity.TryGetValue(m.SelectedEntity, out var b)
                ? new BindingRowView(m.Id, m.SelectedEntity, b.Id, b.TargetTable, b.KeyMappingColumn,
                    b.FieldMappingCount, b.FieldCount, StateOf(b), $"/bindings/{b.Id}")
                : new BindingRowView(m.Id, m.SelectedEntity, null, null, null, 0, null, BindingRowState.Unbound,
                    $"/bindings/new?member={m.Id}"))
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

/// <summary>One Entity on the list. <see cref="BindingId"/> and its details are null when it is not bound.</summary>
public sealed record BindingRowView(
    int MemberId,
    string Entity,
    int? BindingId,
    string? TargetTable,
    string? KeyMappingColumn,
    int FieldMappingCount,
    int? FieldCount,
    BindingRowState State,
    string Href);

/// <summary>
/// Choosing the Target Table for an unbound Entity. <see cref="Member"/> is null when there is no such member on
/// the Primary Channel; <see cref="TargetError"/> is set when the Target Database's tables could not be read.
/// </summary>
public sealed record NewBindingView(
    IReadOnlyList<SetupStepView> Waiting,
    NewBindingMemberView? Member,
    IReadOnlyList<TargetTableOptionView> Tables,
    TargetDatabaseErrorView? TargetError) {

    public static NewBindingView WaitingOn(IReadOnlyList<SetupStepView> missing) => new(missing, null, [], null);

    public static NewBindingView NotFound() => new([], null, [], null);

    public static NewBindingView Unreadable(PlatformEventChannelMemberEntity member, TargetDatabaseErrorView error) =>
        new([], new NewBindingMemberView(member.Id, member.SelectedEntity), [], error);

    /// <summary>Tables whose name matches the Entity first, then free ones, then the ones already bound.</summary>
    public static NewBindingView For(PlatformEventChannelMemberEntity member, IReadOnlyList<TargetTableDTO> tables) {
        var entity = BaseName(member.SelectedEntity);

        return new NewBindingView([], new NewBindingMemberView(member.Id, member.SelectedEntity), tables
            .Select(t => new TargetTableOptionView(t.SchemaName, t.TableName, t.FullName, t.BoundEntityName,
                NameMatches: Matches(entity, Normalize(t.TableName))))
            .OrderByDescending(t => t.NameMatches && t.BoundEntity is null)
            .ThenBy(t => t.BoundEntity is not null)
            .ThenBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList(), null);
    }

    private static bool Matches(string entity, string table) =>
        entity.Length > 0 && (table == entity || table == $"{entity}s" || table == $"{entity}es");

    /// <summary>The Source Object an Entity reports on: AccountChangeEvent is account, Invoice__ChangeEvent is invoice.</summary>
    private static string BaseName(string entity) {
        foreach (var suffix in (string[])["__ChangeEvent", "ChangeEvent"]) {
            if (entity.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                return Normalize(entity[..^suffix.Length]);
            }
        }
        return Normalize(entity);
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

public sealed record NewBindingMemberView(int Id, string Entity);

/// <summary>A table a Binding could write to. <see cref="BoundEntity"/> holds it already when set.</summary>
public sealed record TargetTableOptionView(
    string? SchemaName,
    string TableName,
    string FullName,
    string? BoundEntity,
    bool NameMatches);

/// <summary>One Binding's editor. <see cref="Binding"/> is null when there is no such Binding.</summary>
public sealed record BindingView(IReadOnlyList<SetupStepView> Waiting, BindingEditorView? Binding) {
    public static BindingView WaitingOn(IReadOnlyList<SetupStepView> missing) => new(missing, null);

    public static BindingView NotFound() => new([], null);
}

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
    TargetDatabaseErrorView? TargetError) {

    public static BindingEditorView For(BindingDTO binding, int? memberId, BindingTargetView? target,
        TargetDatabaseErrorView? targetError) => new(
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
        targetError);
}

/// <summary>
/// The parts of the editor read from the Target Database and the Entity's Avro Schema.
/// </summary>
/// <param name="SoftDeleteColumns">Exactly the columns the soft delete setting would accept.</param>
/// <param name="SuggestedKeyColumn">
/// A uniquely-constrained column conventionally named for the Salesforce record ID, offered — never applied —
/// while there is no Key Mapping.
/// </param>
/// <param name="Prefill">
/// The Field Mappings a Binding with none starts its draft from: every name match, so the first visit is a
/// review. Empty once any Field Mapping is saved.
/// </param>
/// <param name="Validation">The stored Binding's validation, as it stands now.</param>
public sealed record BindingTargetView(
    IReadOnlyList<BindableFieldDTO> Fields,
    IReadOnlyList<TargetColumnDTO> Columns,
    IReadOnlyList<string> SoftDeleteColumns,
    string? SuggestedKeyColumn,
    IReadOnlyList<FieldMappingDTO> Prefill,
    BindingValidationDTO Validation) {

    /// <summary>Normalised names conventionally given to the column holding the Salesforce record ID.</summary>
    private static readonly string[] KeyColumnNames = ["sfid", "salesforceid"];

    /// <summary>The Salesforce record ID belongs in the Key Mapping, never in a Field Mapping.</summary>
    private const string RecordIdField = "Id";

    public static BindingTargetView For(BindingDTO binding, IReadOnlyList<BindableFieldDTO> fields,
        IReadOnlyList<TargetColumnDTO> columns, IReadOnlyList<string> softDeleteColumns,
        BindingValidationDTO validation) {
        var suggestedKey = binding.KeyMappingColumn is null
            ? columns.FirstOrDefault(c => c.IsUnique && KeyColumnNames.Contains(Normalize(c.ColumnName)))?.ColumnName
            : null;

        return new BindingTargetView(fields, columns, softDeleteColumns, suggestedKey,
            binding.FieldMappingCount == 0 ? NameMatches(fields, columns, binding.KeyMappingColumn ?? suggestedKey) : [],
            validation);
    }

    private static List<FieldMappingDTO> NameMatches(IReadOnlyList<BindableFieldDTO> fields,
        IReadOnlyList<TargetColumnDTO> columns, string? keyColumn) {
        var existing = columns.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (keyColumn is not null) {
            taken.Add(keyColumn);
        }

        var prefill = new List<FieldMappingDTO>();
        foreach (var field in fields) {
            if (field.Name == RecordIdField || field.SuggestedColumnName is not { } column
                || !existing.Contains(column) || !taken.Add(column)) {
                continue;
            }
            prefill.Add(new FieldMappingDTO { SalesforceFieldName = field.Name, TargetColumnName = column });
        }
        return prefill;
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
