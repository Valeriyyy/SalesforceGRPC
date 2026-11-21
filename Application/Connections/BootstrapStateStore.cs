using Salesforce.Auth;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Application.Connections;

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
