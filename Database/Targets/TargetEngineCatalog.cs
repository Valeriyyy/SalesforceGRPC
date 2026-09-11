using Database.Models;

namespace Database.Targets;

/// <summary>
/// Every engine profile, resolved by engine.
/// </summary>
public interface ITargetEngineCatalog {
    IReadOnlyList<ITargetEngineProfile> All { get; }
    ITargetEngineProfile For(TargetDatabaseEngine engine);
}

/// <inheritdoc />
public sealed class TargetEngineCatalog : ITargetEngineCatalog {
    private readonly IReadOnlyDictionary<TargetDatabaseEngine, ITargetEngineProfile> _byEngine;

    public TargetEngineCatalog(IEnumerable<ITargetEngineProfile> profiles) {
        _byEngine = profiles.ToDictionary(p => p.Engine);

        // Fail at construction, not on the first request for the engine somebody forgot to register.
        var missing = Enum.GetValues<TargetDatabaseEngine>().Where(e => !_byEngine.ContainsKey(e)).ToList();
        if (missing.Count > 0) {
            throw new InvalidOperationException(
                $"No engine profile is registered for: {string.Join(", ", missing)}.");
        }
    }

    public IReadOnlyList<ITargetEngineProfile> All => _byEngine.Values.OrderBy(p => p.Engine).ToList();

    public ITargetEngineProfile For(TargetDatabaseEngine engine) => _byEngine[engine];
}
