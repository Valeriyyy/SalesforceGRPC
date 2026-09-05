using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Application.Connections;

/// <summary>
/// Holds the OAuth <c>state</c> values issued for in-flight Bootstraps.
/// </summary>
/// <remarks>
/// <c>state</c> binds the callback to the request that started it. Without it, anyone who can reach the
/// callback endpoint can hand this application an authorization code of their choosing and have it exchanged
/// against the org's own External Client App.
/// <para>
/// In memory, because the Bootstrap is a single short-lived exchange in a single-instance application, and
/// because a state value that survives a restart is a state value that outlives the browser session it
/// belongs to. A restart mid-Bootstrap means starting it again, which is one click.
/// </para>
/// </remarks>
public interface IBootstrapStateStore {
    /// <summary>Issues a fresh state value and remembers it.</summary>
    string Issue();

    /// <summary>Consumes a state value, returning false if it is unknown, expired, or already used.</summary>
    bool TryConsume(string state);
}

/// <inheritdoc />
public sealed class BootstrapStateStore : IBootstrapStateStore {
    /// <summary>Long enough for a user to read the Salesforce approval screen, short enough to be useless later.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _issued = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public BootstrapStateStore(TimeProvider time) {
        _time = time;
    }

    public string Issue() {
        Prune();

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _issued[state] = _time.GetUtcNow().Add(Lifetime);
        return state;
    }

    public bool TryConsume(string state) {
        Prune();

        // Removed rather than checked, so a replayed callback fails even inside the lifetime window.
        return !string.IsNullOrWhiteSpace(state)
               && _issued.TryRemove(state, out var expiresAt)
               && expiresAt > _time.GetUtcNow();
    }

    private void Prune() {
        var now = _time.GetUtcNow();
        foreach (var (state, expiresAt) in _issued) {
            if (expiresAt <= now) {
                _issued.TryRemove(state, out _);
            }
        }
    }
}
