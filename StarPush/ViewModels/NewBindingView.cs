using Database.Models;
using DTO;

namespace StarPush.ViewModels;

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
