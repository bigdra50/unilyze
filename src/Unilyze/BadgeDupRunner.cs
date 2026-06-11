namespace Unilyze;

internal static class BadgeDupRunner
{
    internal static (ShieldsBadge Badge, StatuslineFormatter.Summary Summary, double DuplicationPercent) Analyze(
        string projectRoot,
        UnilyzeConfig config)
    {
        var dupReport = DupAnalyzer.Analyze(new DupAnalysisOptions(
            projectRoot,
            DupAnalyzer.ResolveMinTokens(config, null),
            DupAnalyzer.ResolveThirdPartyDirs(projectRoot, config, []),
            IncludeThirdParty: false,
            ExcludeDirectories: config.ExcludeDirs,
            ExcludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            ApplyAnyDepthExcludes: !config.DisableDefaultExcludes,
            MaxParallelism: config.MaxParallelism));

        var summary = new StatuslineFormatter.Summary(
            0, 0, 0, 0, dupReport.Summary.AnalyzedFiles, 0, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.Dup, summary, dupReport.Summary.DuplicationPercent);
        return (badge, summary, dupReport.Summary.DuplicationPercent);
    }
}
