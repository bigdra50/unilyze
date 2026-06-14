namespace Unilyze.Serve;

/// <summary>
/// Serializes change-driven analysis and tracks a monotonic input generation. A result is
/// published only if no newer generation arrived while it was being built.
/// </summary>
internal sealed class AnalysisCoordinator : IDisposable
{
    readonly SnapshotStore _store;
    readonly Func<ServeSnapshotContent> _build;
    readonly TimeSpan _debounce;
    readonly Action<ServeSnapshot>? _onPublished;
    readonly Action<ServeAnalysisFailure, Exception>? _onFailed;
    readonly AutoResetEvent _wake = new(false);
    readonly CancellationTokenSource _cts = new();
    readonly object _generationGate = new();
    long _requestedGeneration;
    long _consumedGeneration;
    Thread? _worker;

    public AnalysisCoordinator(
        SnapshotStore store,
        Func<ServeSnapshotContent> build,
        TimeSpan? debounce = null,
        Action<ServeSnapshot>? onPublished = null,
        Action<ServeAnalysisFailure, Exception>? onFailed = null)
    {
        _store = store;
        _build = build;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _onPublished = onPublished;
        _onFailed = onFailed;
    }

    public long RequestAnalysis()
    {
        long generation;
        lock (_generationGate)
            generation = ++_requestedGeneration;
        _wake.Set();
        return generation;
    }

    public void Start(bool requestInitialAnalysis = true)
    {
        _worker = new Thread(Loop) { IsBackground = true, Name = "unilyze-serve-analysis" };
        _worker.Start();
        if (requestInitialAnalysis)
            RequestAnalysis();
    }

    void Loop()
    {
        var waits = new[] { _wake, _cts.Token.WaitHandle };
        while (!_cts.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny(waits) == 1)
                return;

            while (_wake.WaitOne(_debounce))
            {
                if (_cts.IsCancellationRequested)
                    return;
            }

            RunOnce(GetRequestedGeneration());
        }
    }

    void RunOnce(long requestedGeneration)
    {
        lock (_generationGate)
        {
            if (requestedGeneration <= _consumedGeneration)
                return;
        }

        _store.MarkAnalyzing();
        try
        {
            var content = _build();
            ServeSnapshot snapshot;
            lock (_generationGate)
            {
                if (_requestedGeneration > requestedGeneration)
                    return;
                snapshot = _store.PublishSuccess(content);
            }
            _onPublished?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            var failure = ServeAnalysisFailureClassifier.Classify(ex);
            lock (_generationGate)
            {
                if (_requestedGeneration > requestedGeneration)
                    return;
                _store.PublishFailure(failure);
            }
            _onFailed?.Invoke(failure, ex);
        }
        finally
        {
            bool rerun;
            lock (_generationGate)
            {
                _consumedGeneration = requestedGeneration;
                rerun = _requestedGeneration > requestedGeneration;
            }
            if (rerun)
                _wake.Set();
        }
    }

    long GetRequestedGeneration()
    {
        lock (_generationGate)
            return _requestedGeneration;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _wake.Set();
        _worker?.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _wake.Dispose();
    }
}
