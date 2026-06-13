namespace Unilyze.Serve;

/// <summary>
/// Serializes change-driven re-analysis: it coalesces a burst of change events into one
/// run (~300 ms debounce), guarantees a single analysis at a time, and re-runs once more
/// if changes arrived while an analysis was in flight. Each run marks the session
/// analyzing, then atomically publishes success or marks failure (keeping the old snapshot).
/// </summary>
internal sealed class AnalysisCoordinator : IDisposable
{
    readonly SnapshotStore _store;
    readonly Func<ServeSnapshotContent> _build;
    readonly TimeSpan _debounce;
    readonly Action<ServeSnapshot>? _onPublished;
    readonly Action<string>? _onFailed;
    readonly AutoResetEvent _wake = new(false);
    readonly CancellationTokenSource _cts = new();
    Thread? _worker;

    public AnalysisCoordinator(
        SnapshotStore store,
        Func<ServeSnapshotContent> build,
        TimeSpan? debounce = null,
        Action<ServeSnapshot>? onPublished = null,
        Action<string>? onFailed = null)
    {
        _store = store;
        _build = build;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _onPublished = onPublished;
        _onFailed = onFailed;
    }

    /// <summary>Starts the worker and requests an immediate first analysis.</summary>
    public void Start()
    {
        _worker = new Thread(Loop) { IsBackground = true, Name = "unilyze-serve-analysis" };
        _worker.Start();
        RequestAnalysis();
    }

    public void RequestAnalysis() => _wake.Set();

    void Loop()
    {
        var waits = new[] { _wake, _cts.Token.WaitHandle };
        while (!_cts.IsCancellationRequested)
        {
            // Wait for the first change signal (or shutdown).
            if (WaitHandle.WaitAny(waits) == 1)
                return;

            // Debounce: keep extending while more signals arrive within the window.
            while (_wake.WaitOne(_debounce))
            {
                if (_cts.IsCancellationRequested)
                    return;
            }

            RunOnce();
        }
    }

    void RunOnce()
    {
        _store.MarkAnalyzing();
        try
        {
            var content = _build();
            var snapshot = _store.PublishSuccess(content);
            _onPublished?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            // Keep the previous snapshot; surface the failure as stale state.
            _store.PublishFailure(ex.Message);
            _onFailed?.Invoke(ex.Message);
        }
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
