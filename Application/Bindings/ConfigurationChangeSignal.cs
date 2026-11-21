namespace Application.Bindings;

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
