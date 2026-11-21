using Database.Repositories.Interfaces;
using Database.Writeback;
using System.Threading.Channels;

namespace StarPush.Tests.Writeback;

/// <summary>
/// An engine capability whose change stream replays scripted transactions and records what is confirmed,
/// armed and disarmed.
/// </summary>
internal sealed class FakeWritebackCapability : IWritebackCapability {
    private readonly Channel<ChangeTransaction> _pending = Channel.CreateUnbounded<ChangeTransaction>();
    private readonly List<ChangeTransaction> _unconfirmed = [];
    private readonly object _lock = new();
    private readonly List<(WritebackPosition Position, TaskCompletionSource Done)> _confirmWaiters = [];

    public List<WritebackPosition> Confirmed { get; } = [];
    public List<IReadOnlyList<WritebackTable>> Armed { get; } = [];
    public int Disarmed { get; private set; }
    public int StreamsOpened { get; private set; }
    public List<WritebackPrerequisite> Prerequisites { get; } = [];
    public List<string> Log { get; } = [];

    public void Enqueue(ChangeTransaction transaction) => _pending.Writer.TryWrite(transaction);

    public Task WaitForConfirm(WritebackPosition position) {
        lock (_lock) {
            if (Confirmed.Contains(position)) {
                return Task.CompletedTask;
            }
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmWaiters.Add((position, done));
            return done.Task;
        }
    }

    public Task<string?> GetUnavailableReasonAsync(string connectionString, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<IReadOnlyList<WritebackPrerequisite>> CheckPrerequisitesAsync(WritebackEndpoints endpoints,
        IReadOnlyList<WritebackTable> tables, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WritebackPrerequisite>>(Prerequisites);

    public Task PrepareTagAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default) {
        lock (_lock) {
            Log.Add("tag");
        }
        return Task.CompletedTask;
    }

    public Task ArmAsync(WritebackEndpoints endpoints, IReadOnlyList<WritebackTable> tables,
        CancellationToken cancellationToken = default) {
        lock (_lock) {
            Armed.Add(tables);
            Log.Add("arm");
        }
        return Task.CompletedTask;
    }

    public Task DisarmAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default) {
        lock (_lock) {
            Disarmed++;
        }
        return Task.CompletedTask;
    }

    public Task<IWritebackChangeStream> OpenStreamAsync(WritebackEndpoints endpoints,
        CancellationToken cancellationToken = default) {
        lock (_lock) {
            StreamsOpened++;
            // A reopened stream resends everything not yet confirmed, as a replication slot does.
            foreach (var unconfirmed in _unconfirmed.ToList()) {
                _pending.Writer.TryWrite(unconfirmed);
            }
            _unconfirmed.Clear();
        }
        return Task.FromResult<IWritebackChangeStream>(new Stream(this));
    }

    public IRepository CreateTaggedRepository(string connectionString) => throw new NotSupportedException();

    public Task<long?> GetLagBytesAsync(WritebackEndpoints endpoints, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(0);

    public Task<string?> ProveReplicationCredentialAsync(string replicationConnectionString,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    private sealed class Stream(FakeWritebackCapability owner) : IWritebackChangeStream {
        public async IAsyncEnumerable<ChangeTransaction> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            while (await owner._pending.Reader.WaitToReadAsync(cancellationToken)) {
                while (owner._pending.Reader.TryRead(out var transaction)) {
                    lock (owner._lock) {
                        owner._unconfirmed.Add(transaction);
                    }
                    yield return transaction;
                }
            }
        }

        public Task ConfirmAsync(WritebackPosition position, CancellationToken cancellationToken = default) {
            lock (owner._lock) {
                owner.Confirmed.Add(position);
                owner._unconfirmed.RemoveAll(t => t.Position.Value <= position.Value);
                foreach (var waiter in owner._confirmWaiters.Where(w => w.Position.Value <= position.Value).ToList()) {
                    waiter.Done.TrySetResult();
                    owner._confirmWaiters.Remove(waiter);
                }
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
