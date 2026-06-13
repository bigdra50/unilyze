using Unilyze.Discovery;
using Unilyze.Output;
using Unilyze.Config;
using Unilyze.Cli;
using Unilyze.Pipeline;
using System.Text.Json;

namespace Unilyze.Runners;

internal static class MultiProjectAnalyzeRunner
{
    public static int Run(AnalyzeRunContext context)
    {
        var validation = MultiProjectRunnerSupport.ValidateCommon(context.Cli.Opts, context.Cli.ProjectGlobs);
        if (validation is not null)
            return MultiProjectRunnerSupport.Fail(validation, MultiProjectRunnerSupport.ExitUsageError);

        if (context.Format == OutputFormat.Html)
            return MultiProjectRunnerSupport.Fail(
                "HTML output is not supported with --projects. Use -f json or -f sarif.",
                MultiProjectRunnerSupport.ExitUsageError);

        var matchError = MultiProjectRunnerSupport.ValidateMatches(context.Cli.ProjectGlobs, context.Cli.OutputDir);
        if (matchError is not null)
            return MultiProjectRunnerSupport.Fail(matchError, MultiProjectRunnerSupport.ExitUsageError);

        try
        {
            return ExecuteMatches(context);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return MultiProjectRunnerSupport.HandleIoException(ex);
        }
    }

    static int ExecuteMatches(AnalyzeRunContext context)
    {
        var matches = DirectoryGlobMatcher.Expand(context.Cli.ProjectGlobs);
        var entries = new List<MultiProjectSummaryEntry>();
        var toolVersion = MultiProjectRunnerSupport.ToolVersion();

        foreach (var (pattern, rawPath) in matches)
        {
            var request = new ProjectAnalysisRequest(context, pattern, rawPath);
            var work = AnalyzeProject(request);
            if (work is null)
                return MultiProjectRunnerSupport.ExitUsageError;

            entries.Add(MultiProjectSummary.FromAnalysis(work.Name, work.ProjectRoot, work.Result, work.Summary));
            WriteOutput(work, context.Format, context.Cli.OutputDir, matches.Count);
        }

        MultiProjectRunnerSupport.WriteSummaryIfNeeded(context.Cli.OutputDir, toolVersion, entries);
        return 0;
    }

    static ProjectWorkResult? AnalyzeProject(ProjectAnalysisRequest request)
    {
        var context = request.Run;
        var projectRoot = ProgramHelpers.ResolveProjectRoot(request.RawPath);
        var name = DirectoryGlobMatcher.DeriveProjectName(request.Pattern, projectRoot);
        var config = UnilyzeConfig.LoadMerged(projectRoot, context.CliExcludeDirs, context.CliProfile);
        var referenceSettings = ProgramHelpers.LoadReferenceAnalysisSettings(projectRoot, context.Cli.Opts);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            projectRoot, context.Prefix, context.Assembly, config.ExcludeDirs, context.RequestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            analysisConfig: resolved,
            resolveNuget: referenceSettings.ResolveNuget,
            includeGenerated: referenceSettings.IncludeGenerated,
            targetFramework: referenceSettings.TargetFramework);

        if (!MultiProjectRunnerSupport.TryApplyPostProcessing(context.Cli.Opts, config, projectRoot, ref result))
            return null;

        var baselinePath = ProgramHelpers.ResolveBaselineOption(context.Cli.Opts, config);
        var summary = StatuslineFormatter.ComputeSummary(result, baselinePath is not null);
        return new ProjectWorkResult(name, projectRoot, result, summary, resolved);
    }

    static void WriteOutput(ProjectWorkResult work, OutputFormat format, string? outputDir, int matchCount)
    {
        var content = format == OutputFormat.Sarif
            ? SarifFormatter.Generate(work.Result, work.Resolved.Thresholds, work.Name)
            : JsonSerializer.Serialize(work.Result, AnalysisJsonContext.Default.AnalysisResult);

        if (outputDir is not null)
        {
            var extension = format == OutputFormat.Sarif ? ".sarif" : ".json";
            MultiProjectRunnerSupport.WriteProjectOutput(outputDir, work.Name, extension, content);
            return;
        }

        if (matchCount == 1)
            Console.Write(content);
    }
}
