namespace Unilyze.Serve;

/// <summary>
/// Thread-safe holder of the current session state and the latest good snapshot. The
/// snapshot is swapped atomically and only on success; a failed analysis keeps the
/// previous snapshot and marks the session stale (<see cref="ServePhase.Failed"/>).
/// A monotonic generation advances on every transition so long-polls can wake precisely.
/// </summary>
internal sealed class SnapshotStore
{
    readonly object _gate = new();

    long _generation;
    ServePhase _phase = ServePhase.Analyzing;
    ServeSnapshot? _snapshot;
    string? _lastErrorCode;
    string? _lastError;
    DateTimeOffset? _lastSuccessUtc;
    ServeAnalysisMetrics? _lastMetrics;
    TaskCompletionSource<long> _changed = CreateChangeSignal();

    public ServeSnapshot? Current
    {
        get { lock (_gate) return _snapshot; }
    }

    public void MarkAnalyzing()
    {
        lock (_gate)
        {
            _phase = ServePhase.Analyzing;
            Advance();
        }
    }

    public ServeSnapshot PublishSuccess(ServeSnapshotContent content)
    {
        lock (_gate)
        {
            var snapshot = new ServeSnapshot(_generation + 1, content);
            _snapshot = snapshot;
            _phase = ServePhase.Ready;
            _lastErrorCode = null;
            _lastError = null;
            _lastSuccessUtc = content.AnalyzedAtUtc;
            _lastMetrics = content.Metrics;
            Advance();
            return snapshot;
        }
    }

    public void PublishFailure(ServeAnalysisFailure failure)
    {
        lock (_gate)
        {
            _phase = ServePhase.Failed;
            _lastErrorCode = failure.Code;
            _lastError = failure.Summary;
            Advance();
        }
    }

    public ServeStateView GetState()
    {
        lock (_gate)
        {
            return new ServeStateView(
                _generation,
                _phase,
                _snapshot?.Generation,
                _snapshot?.ETag,
                _lastSuccessUtc,
                _lastErrorCode,
                _lastError,
                _lastMetrics);
        }
    }

    public async Task<ServeStateView> WaitForChangeAsync(
        long after,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Task<long> changedTask;
        lock (_gate)
        {
            if (_generation > after)
                return CreateStateView();
            changedTask = _changed.Task;
        }

        try
        {
            await changedTask.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
        }

        lock (_gate)
        {
            return CreateStateView();
        }
    }

    void Advance()
    {
        _generation++;
        var completed = _changed;
        _changed = CreateChangeSignal();
        completed.TrySetResult(_generation);
    }

    ServeStateView CreateStateView() => new(
        _generation,
        _phase,
        _snapshot?.Generation,
        _snapshot?.ETag,
        _lastSuccessUtc,
        _lastErrorCode,
        _lastError,
        _lastMetrics);

    static TaskCompletionSource<long> CreateChangeSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
