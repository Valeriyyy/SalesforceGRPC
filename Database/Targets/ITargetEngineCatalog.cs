using Database.Models;

namespace Database.Targets;

/// <summary>
/// Every engine profile, resolved by engine.
/// </summary>
public interface ITargetEngineCatalog {
    IReadOnlyList<ITargetEngineProfile> All { get; }
    ITargetEngineProfile For(TargetDatabaseEngine engine);
}
