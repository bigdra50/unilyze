namespace Unilyze.Serve;

/// <summary>
/// Serializes watcher notifications and periodic fingerprint reconciliation so one input
/// change advances the analysis generation once.
/// </summary>
internal sealed class ServeChangeWatcher : IDisposable
{
    readonly string _projectRoot;
    readonly Func<long> _onChange;
    readonly Func<IReadOnlyCollection<string>> _resolveExplicitInputs;
    readonly TimeSpan _reconcileInterval;
    readonly SemaphoreSlim _reconcileGate = new(1, 1);
    readonly CancellationTokenSource _cts = new();

    FileSystemWatcher? _watcher;
    Timer? _reconcileTimer;
    string _fingerprint = string.Empty;
    int _disposed;

    public ServeChangeWatcher(
        string projectRoot,
        Func<long> onChange,
        Func<IReadOnlyCollection<string>>? resolveExplicitInputs = null,
        TimeSpan? reconcileInterval = null)
    {
        _projectRoot = projectRoot;
        _onChange = onChange;
        _resolveExplicitInputs = resolveExplicitInputs ?? (() => []);
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromSeconds(2);
    }

    public void Start()
    {
        _reconcileGate.Wait();
        try
        {
            TryStartFileSystemWatcher();
            _fingerprint = ComputeFingerprint();
        }
        finally
        {
            _reconcileGate.Release();
        }

        _reconcileTimer = new Timer(
            _ => _ = ReconcileAsync(),
            null,
            _reconcileInterval,
            _reconcileInterval);
    }

    void TryStartFileSystemWatcher()
    {
        try
        {
            var watcher = new FileSystemWatcher(_projectRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            watcher.Changed += OnFileSystemEvent;
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(
                $"unilyze serve: file watcher unavailable ({ex.Message}); using periodic reconcile only.");
        }
    }

    void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (ServeInputFilter.IsRelevant(e.FullPath, _projectRoot))
            _ = ReconcileAsync();
    }

    void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (ServeInputFilter.IsRelevant(e.FullPath, _projectRoot)
            || ServeInputFilter.IsRelevant(e.OldFullPath, _projectRoot))
            _ = ReconcileAsync();
    }

    void OnWatcherError(object sender, ErrorEventArgs e) =>
        _ = ReconcileAsync(force: true);

    async Task ReconcileAsync(bool force = false)
    {
        try
        {
            await _reconcileGate.WaitAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var current = ComputeFingerprint();
            var previous = Interlocked.Exchange(ref _fingerprint, current);
            if (!_cts.IsCancellationRequested
                && (force || !string.Equals(current, previous, StringComparison.Ordinal)))
                _onChange();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    string ComputeFingerprint() =>
        ServeInputFingerprint.Compute(_projectRoot, _resolveExplicitInputs());

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        try { _watcher?.Dispose(); } catch { /* best effort */ }
        try { _reconcileTimer?.Dispose(); } catch { /* best effort */ }
        _reconcileGate.Wait();
        _reconcileGate.Release();
        _cts.Dispose();
        _reconcileGate.Dispose();
    }
}
