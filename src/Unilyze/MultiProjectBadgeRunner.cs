namespace Unilyze;

internal static class MultiProjectBadgeRunner
{
    public static int Run(string[] args, IReadOnlyDictionary<string, string> opts, IReadOnlyList<string> projectGlobs)
    {
        var cli = new MultiProjectCliContext(opts, projectGlobs);
        var validation = MultiProjectRunnerSupport.ValidateCommon(cli.Opts, cli.ProjectGlobs);
        if (validation is not null)
            return MultiProjectRunnerSupport.Fail(validation, MultiProjectRunnerSupport.ExitUsageError);

        var badgeSetup = TryParseSetup(args, opts, out var setup);
        if (badgeSetup != 0)
            return badgeSetup;

        var matchError = MultiProjectRunnerSupport.ValidateMatches(cli.ProjectGlobs, cli.OutputDir);
        if (matchError is not null)
            return MultiProjectRunnerSupport.Fail(matchError, MultiProjectRunnerSupport.ExitUsageError);

        try
        {
            return ExecuteMatches(new BadgeRunContext(cli, setup));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return MultiProjectRunnerSupport.HandleIoException(ex);
        }
    }

    static int ExecuteMatches(BadgeRunContext context)
    {
        var matches = DirectoryGlobMatcher.Expand(context.Cli.ProjectGlobs);
        var state = ProcessMatches(context, matches);
        if (state.ErrorExit is int exit)
            return exit;

        MultiProjectRunnerSupport.WriteSummaryIfNeeded(
            context.Cli.OutputDir, MultiProjectRunnerSupport.ToolVersion(), state.Entries);

        if (matches.Count > 1 || context.Cli.OutputDir is not null)
            MultiProjectSummary.PrintBadgeTable(state.TableRows);

        if (!state.AnyFailed)
            return 0;

        foreach (var (entry, _) in state.TableRows.Where(r => r.Entry.Gate == "fail"))
            Console.Error.WriteLine($"gate failed: project {entry.Name}");
        return MultiProjectRunnerSupport.ExitGateFailed;
    }

    static BadgeLoopStateResult ProcessMatches(
        BadgeRunContext context,
        IReadOnlyList<(string Pattern, string Path)> matches)
    {
        var entries = new List<MultiProjectSummaryEntry>();
        var tableRows = new List<(MultiProjectSummaryEntry Entry, string MetricValue)>();
        var anyFailed = false;

        foreach (var (pattern, rawPath) in matches)
        {
            var request = new BadgeProjectRequest(context, pattern, rawPath);
            var badgeResult = BuildProject(request);
            if (badgeResult.ErrorExit is int exit)
                return new BadgeLoopStateResult(entries, tableRows, anyFailed, exit);

            var work = badgeResult.Work!;
            if (work.GateLabel == "fail")
                anyFailed = true;

            var entry = MultiProjectSummary.FromAnalysis(
                work.Name, work.ProjectRoot, work.Result, work.Summary, work.GateLabel);
            entries.Add(entry);
            tableRows.Add((entry, MultiProjectSummary.FormatBadgeMetricValue(context.Setup.Metric, work.Summary)));
            WriteOutput(work, context.Setup, context.Cli.OutputDir, matches.Count);
        }

        return new BadgeLoopStateResult(entries, tableRows, anyFailed);
    }

    static (ProjectWorkResult? Work, int? ErrorExit) BuildProject(BadgeProjectRequest request)
    {
        var context = request.Run;
        var projectRoot = ProgramHelpers.ResolveProjectRoot(request.RawPath);
        var name = DirectoryGlobMatcher.DeriveProjectName(request.Pattern, projectRoot);
        var config = UnilyzeConfig.LoadMerged(projectRoot);
        var referenceSettings = ProgramHelpers.LoadReferenceAnalysisSettings(projectRoot, context.Cli.Opts);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            projectRoot, null, null, config.ExcludeDirs, context.Setup.RequestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            analysisConfig: resolved,
            resolveNuget: referenceSettings.ResolveNuget,
            includeGenerated: referenceSettings.IncludeGenerated,
            targetFramework: referenceSettings.TargetFramework);

        var effectiveBaseline = context.Setup.BaselinePath ?? config.Baseline;
        if (!MultiProjectRunnerSupport.TryApplyPostProcessing(
                context.Cli.Opts, config, projectRoot, ref result, effectiveBaseline))
            return (null, MultiProjectRunnerSupport.ExitUsageError);

        var summary = StatuslineFormatter.ComputeSummary(
            result,
            effectiveBaseline is not null,
            context.Setup.UseCodeHealthV1);
        var gate = BadgeGate.Evaluate(
            context.Setup.Metric, summary, context.Setup.FailUnder, context.Setup.FailOver);
        if (gate.Outcome == GateOutcome.UsageError)
            return (null, MultiProjectRunnerSupport.Fail(gate.Message, MultiProjectRunnerSupport.ExitUsageError));

        var gateLabel = MultiProjectSummary.FormatGateOutcome(gate);
        return (new ProjectWorkResult(name, projectRoot, result, summary, resolved, gateLabel), null);
    }

    static void WriteOutput(ProjectWorkResult work, BadgeSetup setup, string? outputDir, int matchCount)
    {
        var badge = BadgeFormatter.Build(setup.Metric, work.Summary);
        var content = setup.Format == BadgeFormat.Svg
            ? BadgeSvgRenderer.Render(badge)
            : BadgeFormatter.Serialize(badge);

        if (outputDir is null)
        {
            if (matchCount == 1)
                Console.Write(content);
            return;
        }

        var extension = setup.Format == BadgeFormat.Svg ? ".svg" : ".json";
        MultiProjectRunnerSupport.WriteProjectOutput(
            outputDir, $"{work.Name}-{setup.MetricSlug}", extension, content);
    }

    static int TryParseSetup(string[] args, IReadOnlyDictionary<string, string> opts, out BadgeSetup setup)
    {
        setup = default!;
        foreach (var gateFlag in new[] { "--fail-under", "--fail-over" })
        {
            if (ProgramHelpers.HasFlagWithoutValue(args, gateFlag))
                return MultiProjectRunnerSupport.Fail($"{gateFlag} requires a value.", MultiProjectRunnerSupport.ExitUsageError);
        }

        var metricStr = opts.GetValueOrDefault("--metric");
        var formatStr = opts.GetValueOrDefault("--format");
        var levelStr = opts.GetValueOrDefault("--level");

        if (!BadgeFormatter.TryParseMetric(metricStr, out var metric))
        {
            Console.Error.WriteLine($"Unknown metric: '{metricStr}'. Valid metrics: codehealth, mi, smells");
            return MultiProjectRunnerSupport.ExitUsageError;
        }

        if (!BadgeFormatter.TryParseFormat(formatStr, out var format))
        {
            Console.Error.WriteLine($"Unknown format: '{formatStr}'. Valid formats: json, svg");
            return MultiProjectRunnerSupport.ExitUsageError;
        }

        AnalysisLevel? requestedLevel = null;
        if (levelStr is not null)
        {
            if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
            {
                Console.Error.WriteLine($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
                return MultiProjectRunnerSupport.ExitUsageError;
            }
            requestedLevel = lvl;
        }

        var optionCheck = BadgeGate.ValidateOptions(
            metric, opts.GetValueOrDefault("--fail-under"), opts.GetValueOrDefault("--fail-over"));
        if (optionCheck.Outcome == GateOutcome.UsageError)
            return MultiProjectRunnerSupport.Fail(optionCheck.Message, MultiProjectRunnerSupport.ExitUsageError);

        setup = new BadgeSetup(
            metric,
            format,
            requestedLevel,
            opts.GetValueOrDefault("--fail-under"),
            opts.GetValueOrDefault("--fail-over"),
            opts.GetValueOrDefault("--baseline"),
            opts.ContainsKey("--codehealth-v1"),
            MultiProjectRunnerSupport.MetricSlug(metric));
        return 0;
    }
}
