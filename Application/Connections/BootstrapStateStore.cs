using Salesforce.Auth;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Application.Connections;

/// <summary>
/// Holds the OAuth <c>state</c> values issued for in-flight Bootstraps, each with its PKCE code verifier.
/// </summary>
/// <remarks>
/// <c>state</c> binds the callback to the request that started it. Without it, anyone who can reach the
/// callback endpoint can hand this application an authorization code of their choosing and have it exchanged
/// against the org's own External Client App. The code verifier is kept with it because the callback is the
/// only place it is needed again, and the state is how the callback is matched to its request.
/// <para>
/// In memory, because the Bootstrap is a single short-lived exchange in a single-instance application, and
/// because a state value that survives a restart is a state value that outlives the browser session it
/// belongs to. A restart mid-Bootstrap means starting it again, which is one click.
/// </para>
/// </remarks>
public interface IBootstrapStateStore {
    /// <summary>Issues a fresh state value and code verifier, and remembers them together.</summary>
    BootstrapRequest Issue();

    /// <summary>
    /// Consumes a state value and hands back its code verifier, returning false if the state is unknown,
    /// expired, or already used.
    /// </summary>
    bool TryConsume(string state, [NotNullWhen(true)] out string? codeVerifier);
}

/// <summary>What one Bootstrap's authorize request needs, and its callback must present again.</summary>
public sealed record BootstrapRequest(string State, string CodeVerifier);

/// <inheritdoc />
public sealed class BootstrapStateStore : IBootstrapStateStore {
    /// <summary>Long enough for a user to read the Salesforce approval screen, short enough to be useless later.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, string CodeVerifier)> _issued =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public BootstrapStateStore(TimeProvider time) {
        _time = time;
    }

    public BootstrapRequest Issue() {
        Prune();

        var request = new BootstrapRequest(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Pkce.NewVerifier());
        _issued[request.State] = (_time.GetUtcNow().Add(Lifetime), request.CodeVerifier);
        return request;
    }

    public bool TryConsume(string state, [NotNullWhen(true)] out string? codeVerifier) {
        Prune();

        // Removed rather than checked, so a replayed callback fails even inside the lifetime window.
        if (!string.IsNullOrWhiteSpace(state) && _issued.TryRemove(state, out var issued)
            && issued.ExpiresAt > _time.GetUtcNow()) {
            codeVerifier = issued.CodeVerifier;
            return true;
        }

        codeVerifier = null;
        return false;
    }

    private void Prune() {
        var now = _time.GetUtcNow();
        foreach (var (state, issued) in _issued) {
            if (issued.ExpiresAt <= now) {
                _issued.TryRemove(state, out _);
            }
        }
    }
}
