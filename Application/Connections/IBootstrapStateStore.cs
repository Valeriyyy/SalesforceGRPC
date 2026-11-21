using System.Diagnostics.CodeAnalysis;

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
