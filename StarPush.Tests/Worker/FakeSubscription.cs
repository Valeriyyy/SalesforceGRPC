using Grpc.Core;
using GrpcClient;

namespace StarPush.Tests.Worker;

/// <summary>One scripted subscription: what the client asked for, and what it was sent.</summary>
internal sealed class FakeSubscription : IClientStreamWriter<FetchRequest> {
    private readonly object _gate = new();
    private readonly Queue<FetchResponse> _responses;
    private readonly RpcException? _failure;
    private readonly List<FetchRequest> _requests = [];
    private TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _outstanding;

    public FakeSubscription(IEnumerable<FetchResponse> responses, RpcException? failure) {
        _responses = new Queue<FetchResponse>(responses);
        _failure = failure;
    }

    /// <summary>Every FetchRequest the client wrote, in order.</summary>
    public IReadOnlyList<FetchRequest> Requests {
        get { lock (_gate) { return _requests.ToList(); } }
    }

    /// <summary>The most events the client ever had requested and not yet received.</summary>
    public int MaxOutstanding { get; private set; }

    /// <summary>
    /// Completes when the client comes back for more after the last scripted response — that is, once it has
    /// finished with every response it was sent.
    /// </summary>
    public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(FetchRequest message, CancellationToken cancellationToken) => WriteAsync(message);

    public Task WriteAsync(FetchRequest message) {
        TaskCompletionSource requested;
        lock (_gate) {
            _requests.Add(message);
            _outstanding += message.NumRequested;
            MaxOutstanding = Math.Max(MaxOutstanding, _outstanding);
            requested = _requested;
            _requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        requested.TrySetResult();
        return Task.CompletedTask;
    }

    public Task CompleteAsync() => Task.CompletedTask;

    public IAsyncStreamReader<FetchResponse> ReaderFor(CancellationToken callToken) => new Reader(this, callToken);

    private async Task<FetchResponse?> NextAsync(CancellationToken cancellationToken) {
        while (true) {
            Task requested;
            lock (_gate) {
                if (_responses.Count == 0) {
                    break;
                }

                var next = _responses.Peek();
                if (next.Events.Count <= _outstanding && _requests.Count > 0) {
                    _responses.Dequeue();
                    _outstanding -= next.Events.Count;
                    next.PendingNumRequested = _outstanding;
                    return next;
                }
                requested = _requested.Task;
            }
            await requested.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        Drained.TrySetResult();
        if (_failure is not null) {
            throw _failure;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private sealed class Reader : IAsyncStreamReader<FetchResponse> {
        private readonly FakeSubscription _owner;
        private readonly CancellationToken _callToken;

        public Reader(FakeSubscription owner, CancellationToken callToken) {
            _owner = owner;
            _callToken = callToken;
        }

        public FetchResponse Current { get; private set; } = null!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken) {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_callToken, cancellationToken);
            FetchResponse? next;
            try {
                next = await _owner.NextAsync(linked.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (_callToken.IsCancellationRequested) {
                // What a real gRPC call does when the token it was opened with is cancelled.
                throw new RpcException(new Status(StatusCode.Cancelled, "Call canceled by the client."));
            }
            if (next is null) {
                return false;
            }
            Current = next;
            return true;
        }
    }
}
