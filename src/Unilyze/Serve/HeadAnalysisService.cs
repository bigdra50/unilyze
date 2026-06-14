using Unilyze.History;
using Unilyze.Pipeline;
using Unilyze.Cli;
using Unilyze.Config;

namespace Unilyze.Serve;

internal sealed record HeadAnalysisResult(
    string HeadOid,
    AnalysisResult Analysis,
    string? AnalysisLevel);

internal sealed class HeadAnalysisService : IDisposable
{
    readonly string _projectRoot;
    readonly ServeOptions _options;
    readonly TimeSpan _pollInterval;
    readonly CancellationTokenSource _cts = new();
    readonly object _gate = new();

    HeadAnalysisResult? _cached;
    Timer? _timer;

    public HeadAnalysisService(string projectRoot, ServeOptions options, TimeSpan? pollInterval = null)
    {
        _projectRoot = projectRoot;
        _options = options;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public HeadAnalysisResult? Current
    {
        get { lock (_gate) return _cached; }
    }

    public void Start()
    {
        _timer = new Timer(_ => PollOnce(), null, TimeSpan.Zero, _pollInterval);
    }

    void PollOnce()
    {
        if (_cts.IsCancellationRequested)
            return;

        try
        {
            var currentOid = GetHeadOid();
            if (currentOid is null)
                return;

            lock (_gate)
            {
                if (_cached?.HeadOid == currentOid)
                    return;
            }

            var analysis = AnalyzeHead(currentOid);
            if (analysis is null)
                return;

            var verifyOid = GetHeadOid();
            if (verifyOid != currentOid)
                return;

            lock (_gate)
            {
                _cached = analysis;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"HEAD analysis failed: {ex.Message}");
        }
    }

    string? GetHeadOid()
    {
        try
        {
            return GitProcess.Run(_projectRoot, "rev-parse", "HEAD").Trim();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    HeadAnalysisResult? AnalyzeHead(string oid)
    {
        GitWorktreeSession? session = null;
        try
        {
            session = GitWorktreeSession.Create(_projectRoot, "HEAD");
            var relative = GitWorktreeSession.GetRepoRelativePath(_projectRoot);
            var worktreeProjectPath = string.IsNullOrEmpty(relative)
                ? session.WorktreePath
                : Path.GetFullPath(Path.Combine(session.WorktreePath, relative));

            var projectRoot = ProgramHelpers.ResolveProjectRoot(worktreeProjectPath);
            var config = UnilyzeConfig.LoadMerged(projectRoot, _options.ExcludeDirs, _options.Profile);
            var resolved = config.ResolveAnalysisConfig();

            var result = AnalysisPipeline.Build(
                worktreeProjectPath,
                _options.Prefix,
                _options.Assembly,
                config.ExcludeDirs,
                _options.RequestedLevel,
                excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
                applyAnyDepthExcludes: !config.DisableDefaultExcludes,
                analysisConfig: resolved,
                maxParallelism: config.MaxParallelism,
                incremental: false);

            return new HeadAnalysisResult(oid, result, result.AnalysisLevel);
        }
        catch (Exception ex) when (ex is GitWorktreeException or InvalidOperationException)
        {
            Console.Error.WriteLine($"HEAD worktree analysis skipped: {ex.Message}");
            return null;
        }
        finally
        {
            session?.Dispose();
        }
    }

    public bool LevelsMatch(string? currentLevel)
    {
        var head = Current;
        if (head is null)
            return false;
        return string.Equals(head.AnalysisLevel, currentLevel, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer?.Dispose();
        _cts.Dispose();
    }
}
