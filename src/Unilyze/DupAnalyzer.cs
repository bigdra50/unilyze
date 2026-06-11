namespace Unilyze;

internal static class DupAnalyzer
{
    public const int DefaultMinTokens = 100;

    public static CloneReport Analyze(DupAnalysisOptions options)
    {
        var projectRoot = ProgramHelpers.ResolveProjectRoot(options.Path);
        var buildOptions = new AnalysisBuildOptions(
            options.Path,
            ExcludeDirectories: options.ExcludeDirectories,
            ExcludeGeneratedCode: options.ExcludeGeneratedCode,
            ApplyAnyDepthExcludes: options.ApplyAnyDepthExcludes,
            MaxParallelism: options.MaxParallelism);

        var discover = AnalysisPipelineDiscovery.Discover(buildOptions);
        var (_, trees) = AnalysisPipelineDiscovery.CollectTypes(discover, buildOptions);
        var files = CloneTokenizer.Tokenize(trees);
        var rawClasses = CloneDetector.Detect(files, options.MinTokens);
        var (classes, suppressedPairCount) = ThirdPartyCloneFilter.Apply(
            rawClasses,
            ThirdPartyCloneFilter.ResolveRoots(projectRoot, options.ThirdPartyDirs),
            options.IncludeThirdParty);
        var summary = CloneReportBuilder.BuildSummary(files, classes, suppressedPairCount, options.MinTokens);
        var toolVersion = typeof(DupAnalyzer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        return new CloneReport(
            projectRoot,
            toolVersion,
            AnalysisResult.CurrentMetricsVersion,
            summary,
            classes);
    }

    public static int ResolveMinTokens(UnilyzeConfig config, string? cliValue)
    {
        if (cliValue is not null && int.TryParse(cliValue, out var parsed) && parsed > 0)
            return parsed;

        if (config.Dup?.MinTokens is > 0)
            return config.Dup.MinTokens.Value;

        return DefaultMinTokens;
    }

    public static IReadOnlyList<string> ResolveThirdPartyDirs(
        string projectRoot,
        UnilyzeConfig config,
        IReadOnlyList<string> cliDirs)
    {
        var merged = new List<string>(ThirdPartyCloneFilter.DefaultRelativeDirs);
        if (config.Dup?.ThirdPartyDirs is { Count: > 0 })
            merged.AddRange(config.Dup.ThirdPartyDirs);
        if (cliDirs.Count > 0)
            merged.AddRange(cliDirs);

        return merged
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
