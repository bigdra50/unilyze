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
    string? _lastError;
    DateTimeOffset? _lastSuccessUtc;
    ServeAnalysisMetrics? _lastMetrics;

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
            _lastError = null;
            _lastSuccessUtc = content.AnalyzedAtUtc;
            _lastMetrics = content.Metrics;
            Advance();
            return snapshot;
        }
    }

    public void PublishFailure(string error)
    {
        lock (_gate)
        {
            _phase = ServePhase.Failed;
            _lastError = error;
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
                _lastError,
                _lastMetrics);
        }
    }

    /// <summary>
    /// Blocks until the generation exceeds <paramref name="after"/> or the timeout elapses,
    /// then returns the current state. This is the server side of the ETag long-poll.
    /// </summary>
    public ServeStateView WaitForChange(long after, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_generation <= after)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;
                Monitor.Wait(_gate, remaining);
            }

            return new ServeStateView(
                _generation,
                _phase,
                _snapshot?.Generation,
                _snapshot?.ETag,
                _lastSuccessUtc,
                _lastError,
                _lastMetrics);
        }
    }

    void Advance()
    {
        _generation++;
        Monitor.PulseAll(_gate);
    }
}
