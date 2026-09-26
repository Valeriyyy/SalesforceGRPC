using Grpc.Core;
using GrpcClient;

namespace SalesforceGRPCTest.Worker;

/// <summary>
/// A scripted Salesforce Pub/Sub server. Each <c>Subscribe</c> call takes the next script in order; once the
/// scripts run out, a subscription delivers nothing and waits to be cancelled, the way a quiet stream does.
/// </summary>
/// <remarks>
/// It honours flow control the way Salesforce does: a response is delivered only once the client has
/// requested at least as many events as it carries, so a worker that stops asking stops receiving.
/// </remarks>
internal sealed class FakePubSub : PubSub.PubSubClient {
    private readonly object _gate = new();
    private readonly Queue<FakeSubscription> _scripts = new();
    private readonly List<FakeSubscription> _opened = [];
    private TaskCompletionSource _nextOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Queues a subscription that delivers these responses, then goes quiet.</summary>
    public FakeSubscription Script(params FetchResponse[] responses) => Enqueue(new FakeSubscription(responses, null));

    /// <summary>Queues a subscription that delivers these responses, then fails with the given error.</summary>
    public FakeSubscription ScriptFailure(RpcException failure, params FetchResponse[] responses) =>
        Enqueue(new FakeSubscription(responses, failure));

    /// <summary>Every subscription opened so far, in order.</summary>
    public IReadOnlyList<FakeSubscription> Opened {
        get { lock (_gate) { return _opened.ToList(); } }
    }

    /// <summary>Completes once at least <paramref name="count"/> subscriptions have been opened.</summary>
    public async Task WaitForSubscriptionsAsync(int count) {
        while (true) {
            Task next;
            lock (_gate) {
                if (_opened.Count >= count) {
                    return;
                }
                next = _nextOpened.Task;
            }
            await next.ConfigureAwait(false);
        }
    }

    private FakeSubscription Enqueue(FakeSubscription subscription) {
        lock (_gate) {
            _scripts.Enqueue(subscription);
        }
        return subscription;
    }

    public override AsyncDuplexStreamingCall<FetchRequest, FetchResponse> Subscribe(Metadata? headers = null,
        DateTime? deadline = null, CancellationToken cancellationToken = default) {
        FakeSubscription subscription;
        TaskCompletionSource opened;
        lock (_gate) {
            subscription = _scripts.Count > 0 ? _scripts.Dequeue() : new FakeSubscription([], null);
            _opened.Add(subscription);
            opened = _nextOpened;
            _nextOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        opened.TrySetResult();

        return new AsyncDuplexStreamingCall<FetchRequest, FetchResponse>(
            subscription, subscription.ReaderFor(cancellationToken),
            Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }

    public override AsyncUnaryCall<SchemaInfo> GetSchemaAsync(SchemaRequest request, Metadata? headers = null,
        DateTime? deadline = null, CancellationToken cancellationToken = default) =>
        throw new RpcException(new Status(StatusCode.NotFound, $"No schema {request.SchemaId} in this script"));
}

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
            var next = await _owner.NextAsync(linked.Token).ConfigureAwait(false);
            if (next is null) {
                return false;
            }
            Current = next;
            return true;
        }
    }
}
