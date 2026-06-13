namespace Unilyze.Serve;

/// <summary>
/// Detects analysis-input changes two ways: a <see cref="FileSystemWatcher"/> for
/// immediacy, and a low-frequency fingerprint reconcile that recovers events the
/// watcher dropped. Every relevant notification (and every fingerprint divergence)
/// invokes <c>onChange</c>; downstream coalescing collapses bursts into one analysis.
/// </summary>
internal sealed class ServeChangeWatcher : IDisposable
{
    readonly string _projectRoot;
    readonly Action _onChange;
    readonly TimeSpan _reconcileInterval;
    readonly object _gate = new();

    FileSystemWatcher? _watcher;
    Timer? _reconcileTimer;
    string _fingerprint = string.Empty;
    bool _disposed;

    public ServeChangeWatcher(string projectRoot, Action onChange, TimeSpan? reconcileInterval = null)
    {
        _projectRoot = projectRoot;
        _onChange = onChange;
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromSeconds(2);
    }

    public void Start()
    {
        _fingerprint = ServeInputFingerprint.Compute(_projectRoot);
        TryStartFileSystemWatcher();

        _reconcileTimer = new Timer(_ => Reconcile(), null, _reconcileInterval, _reconcileInterval);
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
            // Fall back to reconcile-only mode (e.g., inotify watch limit reached).
            Console.Error.WriteLine($"unilyze serve: file watcher unavailable ({ex.Message}); using periodic reconcile only.");
        }
    }

    void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (ServeInputFilter.IsRelevant(e.FullPath, _projectRoot))
            _onChange();
    }

    void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (ServeInputFilter.IsRelevant(e.FullPath, _projectRoot)
            || ServeInputFilter.IsRelevant(e.OldFullPath, _projectRoot))
            _onChange();
    }

    void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow: events were dropped. Force a reconcile to catch up.
        Reconcile(force: true);
    }

    void Reconcile(bool force = false)
    {
        string current;
        try
        {
            current = ServeInputFingerprint.Compute(_projectRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = force || !string.Equals(current, _fingerprint, StringComparison.Ordinal);
            _fingerprint = current;
        }

        if (changed)
            _onChange();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        try { _watcher?.Dispose(); } catch { /* best effort */ }
        try { _reconcileTimer?.Dispose(); } catch { /* best effort */ }
    }
}
