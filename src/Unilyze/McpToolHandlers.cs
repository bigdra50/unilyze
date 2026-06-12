using System.Text;
using System.Text.Json;

namespace Unilyze;

internal sealed class McpToolHandlers
{
    readonly McpAnalysisCache _cache = new();

    public McpToolResult Call(string name, McpToolArgs args)
    {
        try
        {
            var text = name switch
            {
                "analyze" => HandleAnalyze(args),
                "get_summary" => HandleGetSummary(args),
                "worst_types" => HandleWorstTypes(args),
                "query_type" => HandleQueryType(args),
                "diff" => HandleDiff(args),
                "hotspot" => HandleHotspot(args),
                "baseline_status" => HandleBaselineStatus(args),
                "triage_add" => HandleTriageAdd(args),
                "schema" => HandleSchema(args),
                "version" => HandleVersion(args),
                _ => throw new InvalidOperationException($"Unknown tool: {name}"),
            };
            var maxChars = McpResponseTrimmer.ResolveMaxChars(args);
            return McpToolResult.Success(McpResponseTrimmer.Apply(text, maxChars));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException
                                 or IOException or UnauthorizedAccessException or GitWorktreeException)
        {
            return McpToolResult.Error(ex.Message);
        }
    }

    string HandleAnalyze(McpToolArgs args)
    {
        var analysis = _cache.Load(args);
        return McpAnalyzeSummary.ToMarkdown(analysis);
    }

    string HandleGetSummary(McpToolArgs args)
    {
        var analysis = _cache.Load(args);
        return McpAnalyzeSummary.ToMarkdown(analysis);
    }

    string HandleWorstTypes(McpToolArgs args)
    {
        var analysis = _cache.Load(args);
        var count = args.TryGetInt("count", out var n) && n > 0 ? n : 5;
        var format = args.GetString("format") ?? "md";
        var selection = QuerySelector.SelectWorst(analysis.TypeMetrics ?? [], count);
        if (selection.AmbiguityMessage is not null)
            return selection.AmbiguityMessage;

        var queryResult = QueryEvidenceAssembler.Build(analysis, selection.Types);
        return FormatQueryResult(queryResult, format);
    }

    string HandleQueryType(McpToolArgs args)
    {
        var typeName = args.GetString("type")
            ?? throw new ArgumentException("query_type requires a 'type' argument.");
        var analysis = _cache.Load(args);
        var selection = QuerySelector.SelectByName(analysis.TypeMetrics ?? [], typeName);
        if (selection.AmbiguityMessage is not null)
            return selection.AmbiguityMessage;

        if (selection.Types.Count == 0)
            return selection.AmbiguityMessage ?? $"Type not found: '{typeName}'";

        var format = args.GetString("format") ?? "md";
        var queryResult = QueryEvidenceAssembler.Build(analysis, selection.Types);
        return FormatQueryResult(queryResult, format);
    }

    string HandleDiff(McpToolArgs args)
    {
        var afterInput = args.GetString("after_input") ?? args.Input;
        if (afterInput is null)
            throw new ArgumentException("diff requires 'after_input' or 'input' for the after snapshot.");

        var after = LoadSnapshot(afterInput);
        var before = LoadBeforeSnapshot(args, after);
        var diff = DiffCalculator.Compare(before, after);
        var beforeSummary = StatuslineFormatter.ComputeSummary(before);
        var afterSummary = StatuslineFormatter.ComputeSummary(after);
        return MarkdownDiffFormatter.Generate(diff, beforeSummary, afterSummary, gate: null);
    }

    string HandleHotspot(McpToolArgs args)
    {
        var analysis = _cache.Load(args);
        var path = analysis.ProjectPath;
        var since = args.GetString("since") ?? "12.month";
        var topN = args.TryGetInt("top_n", out var n) && n > 0 ? n : 20;
        var typeMetrics = analysis.TypeMetrics ?? [];

        var gitLogOutput = HotspotAnalyzer.RunGitLog(path, since);
        var allCommits = HotspotAnalyzer.ParseCommitLog(gitLogOutput);
        var (includedCommits, botExcluded) = HotspotAnalyzer.ApplyBotFilter(
            allCommits, BotAuthorMatcher.CreateDefault(), botFilterEnabled: true);
        var changeFrequencies = HotspotAnalyzer.AggregateFileChanges(includedCommits, halfLife: null);
        var context = new HotspotAnalysisContext(
            path, since, topN, BotFilter: true, BotCommitsExcluded: botExcluded,
            HalfLife: null, HalfLifeSpan: null);
        var hotspot = HotspotAnalyzer.Analyze(typeMetrics, changeFrequencies, context);
        return FormatHotspots(hotspot);
    }

    string HandleBaselineStatus(McpToolArgs args)
    {
        var projectRoot = ProgramHelpers.ResolveProjectRoot(args.PathOrDefault());
        var baselinePath = Path.Combine(projectRoot, ".unilyze", "baseline.json");
        if (!File.Exists(baselinePath))
        {
            return JsonSerializer.Serialize(new
            {
                present = false,
                path = baselinePath,
                message = "No baseline file found.",
            });
        }

        var baseline = BaselineFile.Load(baselinePath);
        var analysis = TryLoadCachedAnalysis(args);
        return JsonSerializer.Serialize(new
        {
            present = true,
            path = baselinePath,
            createdAt = baseline.CreatedAt,
            fingerprintCount = baseline.Fingerprints.Count,
            metricsVersion = baseline.MetricsVersion,
            toolVersion = baseline.ToolVersion,
            suppressedCount = analysis?.SuppressedCount,
        });
    }

    string HandleTriageAdd(McpToolArgs args)
    {
        var id = args.GetString("id")
            ?? throw new ArgumentException("triage_add requires an 'id' argument.");
        var verdict = args.GetString("verdict")
            ?? throw new ArgumentException("triage_add requires a 'verdict' argument.");
        if (!TriageVerdicts.IsKnown(verdict))
            throw new ArgumentException(
                $"Unknown verdict: '{verdict}'. Valid verdicts: {string.Join(", ", TriageVerdicts.All)}");

        var projectRoot = ProgramHelpers.ResolveProjectRoot(args.PathOrDefault());
        var outputPath = args.GetString("triage_output") ?? TriageFile.DefaultPath(projectRoot);
        if (!Path.IsPathRooted(outputPath))
            outputPath = Path.GetFullPath(Path.Combine(projectRoot, outputPath));

        var triage = File.Exists(outputPath) ? TriageFile.Load(outputPath) : TriageFile.CreateEmpty();
        var entry = new TriageEntry(
            id, verdict, args.GetString("reason"), args.GetString("by"), DateTimeOffset.UtcNow);
        triage = triage.Upsert(entry);
        TriageFile.Save(outputPath, triage);
        return JsonSerializer.Serialize(new
        {
            written = outputPath,
            id,
            verdict,
        });
    }

    string HandleSchema(McpToolArgs args) => EmbeddedCliText.Schema;

    string HandleVersion(McpToolArgs args) =>
        JsonSerializer.Serialize(new
        {
            toolVersion = ToolVersionInfo.Current,
            metricsVersion = AnalysisResult.CurrentMetricsVersion,
        });

    AnalysisResult? TryLoadCachedAnalysis(McpToolArgs args)
    {
        try
        {
            return args.Input is not null || Directory.Exists(args.PathOrDefault())
                ? _cache.Load(args)
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    static string FormatQueryResult(QueryResult queryResult, string format) =>
        format.ToLowerInvariant() switch
        {
            "md" or "markdown" => QueryEvidenceFormatter.ToMarkdown(queryResult),
            "json" => QueryEvidenceFormatter.ToJson(queryResult),
            _ => throw new ArgumentException($"Unknown format: '{format}'. Valid formats: md, json"),
        };

    static AnalysisResult LoadSnapshot(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)
               ?? throw new InvalidOperationException($"Failed to parse: {path}");
    }

    static AnalysisResult LoadBeforeSnapshot(McpToolArgs args, AnalysisResult after)
    {
        var beforeInput = args.GetString("before_input");
        if (beforeInput is not null)
            return LoadSnapshot(beforeInput);

        var baseRef = args.GetString("base_ref");
        if (baseRef is null)
            throw new ArgumentException("diff requires 'before_input' or 'base_ref'.");

        var projectPath = args.GetString("path") ?? after.ProjectPath;
        GitWorktreeSession? session = null;
        try
        {
            session = GitWorktreeSession.Create(projectPath, baseRef);
            var baseProjectPath = ResolveBaseProjectPath(session, projectPath);
            var projectRoot = ProgramHelpers.ResolveProjectRoot(baseProjectPath);
            var config = UnilyzeConfig.LoadMerged(projectRoot, []);
            var resolved = config.ResolveAnalysisConfig();
            return AnalysisPipeline.Build(
                baseProjectPath,
                null,
                null,
                config.ExcludeDirs,
                requestedLevel: null,
                excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
                applyAnyDepthExcludes: !config.DisableDefaultExcludes,
                analysisConfig: resolved,
                maxParallelism: config.MaxParallelism);
        }
        finally
        {
            session?.Dispose();
        }
    }

    static string ResolveBaseProjectPath(GitWorktreeSession session, string projectPath)
    {
        var relative = GitWorktreeSession.GetRepoRelativePath(projectPath);
        return string.IsNullOrEmpty(relative)
            ? session.WorktreePath
            : Path.GetFullPath(Path.Combine(session.WorktreePath, relative));
    }

    static string FormatHotspots(HotspotResult hotspot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Hotspots");
        sb.AppendLine();
        sb.AppendLine($"Project: `{hotspot.ProjectPath}` | Since: {hotspot.Since} | Top: {hotspot.TopN}");
        if (hotspot.BotFilter)
            sb.AppendLine($"Bot commits excluded: {hotspot.BotCommitsExcluded}");
        sb.AppendLine();
        if (hotspot.Hotspots.Count == 0)
        {
            sb.AppendLine("_No hotspots found._");
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        sb.AppendLine("| Rank | Score | Churn | Health | Type |");
        sb.AppendLine("| ---: | ----: | ----: | -----: | ---- |");
        for (var i = 0; i < hotspot.Hotspots.Count; i++)
        {
            var h = hotspot.Hotspots[i];
            var typeName = string.IsNullOrEmpty(h.Namespace) ? h.TypeName : $"{h.Namespace}.{h.TypeName}";
            sb.AppendLine($"| {i + 1} | {h.HotspotScore:F1} | {h.ChangeCount} | {h.CodeHealth:F1} | {typeName} |");
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}

internal readonly record struct McpToolResult(string Text, bool IsError)
{
    public static McpToolResult Success(string text) => new(text, false);
    public static McpToolResult Error(string text) => new(text, true);
}
