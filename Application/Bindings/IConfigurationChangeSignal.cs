namespace Application.Bindings;

/// <summary>
/// Tells the worker that stored configuration changed and its subscription plan is stale.
/// </summary>
/// <remarks>
/// The caches the worker reads through have a one-hour sliding expiration, and the worker additionally holds
/// its schema dictionary for the life of a stream — so invalidating a cache alone would leave a user watching
/// events flow into the old shape for up to an hour after saving. This signal closes that gap.
///
/// It works because the worker and the API share a process. Splitting them would make this a distributed
/// invalidation problem and the design would need revisiting.
///
/// It covers Bindings and the Org Connection alike. Naming it for Bindings alone, as it once was, invited a
/// second parallel signal for connection changes — two things to remember to raise, and one of them
/// eventually forgotten.
/// </remarks>
public interface IConfigurationChangeSignal {
    /// <summary>Marks the current plan stale and wakes anything waiting.</summary>
    void Signal();

    /// <summary>
    /// Completes the next time <see cref="Signal"/> is called, or when the token is cancelled.
    /// </summary>
    Task WaitForChangeAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ConfigurationChangeSignal : IConfigurationChangeSignal {
    private TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Signal() {
        // Swap in a fresh source before completing the old one, so a waiter that re-subscribes immediately
        // waits on the next change rather than seeing this one again.
        var previous = Interlocked.Exchange(ref _pending, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }

    public Task WaitForChangeAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _pending).Task.WaitAsync(cancellationToken);
}
