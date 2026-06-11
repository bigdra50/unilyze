namespace Unilyze;

internal static class BadgeRunner
{
    const int ExitUsageError = 1;
    const int ExitGateFailed = 2;

    public static int Run(string[] args)
    {
        if (ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateBadgeArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);
        var projectGlobs = ProgramHelpers.ParseMultiValueOption(args, "--projects");
        if (projectGlobs.Count > 0)
            return MultiProjectRunner.RunBadge(args, opts, projectGlobs);

        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var metricStr = opts.GetValueOrDefault("--metric");
        var formatStr = opts.GetValueOrDefault("--format");
        var levelStr = opts.GetValueOrDefault("--level");
        var failUnder = opts.GetValueOrDefault("--fail-under");
        var failOver = opts.GetValueOrDefault("--fail-over");
        var baselinePath = opts.GetValueOrDefault("--baseline");

        if (ProgramHelpers.HasFlagWithoutValue(args, "--baseline"))
        {
            Console.Error.WriteLine("--baseline requires a file path.");
            return ExitUsageError;
        }

        if (!BadgeFormatter.TryParseMetric(metricStr, out var metric))
        {
            Console.Error.WriteLine($"Unknown metric: '{metricStr}'. Valid metrics: codehealth, mi, smells");
            return 1;
        }

        if (!BadgeFormatter.TryParseFormat(formatStr, out var format))
        {
            Console.Error.WriteLine($"Unknown format: '{formatStr}'. Valid formats: json, svg");
            return 1;
        }

        AnalysisLevel? requestedLevel = null;
        if (levelStr != null)
        {
            if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
            {
                Console.Error.WriteLine($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
                return 1;
            }
            requestedLevel = lvl;
        }

        // ParseOptions drops a value-taking flag that has no value token, which
        // would silently skip the gate (false green in CI). Reject it explicitly.
        foreach (var gateFlag in new[] { "--fail-under", "--fail-over" })
        {
            if (ProgramHelpers.HasFlagWithoutValue(args, gateFlag))
                return Fail($"{gateFlag} requires a value.", ExitUsageError);
        }

        // Fail fast on incompatible gate flags before running analysis.
        var optionCheck = BadgeGate.ValidateOptions(metric, failUnder, failOver);
        if (optionCheck.Outcome == GateOutcome.UsageError)
            return Fail(optionCheck.Message, ExitUsageError);


        try
        {
            var fullPath = ProgramHelpers.ResolveProjectRoot(path);
            var config = UnilyzeConfig.LoadMerged(fullPath);
            var resolved = config.ResolveAnalysisConfig();
            var result = AnalysisPipeline.Build(
                fullPath, null, null, config.ExcludeDirs, requestedLevel,
                excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
                applyAnyDepthExcludes: !config.DisableDefaultExcludes,
                analysisConfig: resolved,
                maxParallelism: config.MaxParallelism);

            var effectiveBaseline = baselinePath ?? config.Baseline;
            var baselineError = ProgramHelpers.TryApplyBaseline(result, fullPath, effectiveBaseline, out result);
            if (baselineError is 1)
                return ExitUsageError;

            var triagePath = TriageApplication.ResolvePath(opts, config, fullPath);
            var triageError = TriageApplication.TryApply(result, triagePath, out result);
            if (triageError is 1)
                return ExitUsageError;

            var excludeBaselined = effectiveBaseline is not null;
            var summary = StatuslineFormatter.ComputeSummary(result, excludeBaselined);
            var badge = BadgeFormatter.Build(metric, summary);
            var content = format == BadgeFormat.Svg ? BadgeSvgRenderer.Render(badge) : BadgeFormatter.Serialize(badge);

            // Emit the badge unchanged (backward compatible) before evaluating the gate.
            if (output != null)
            {
                File.WriteAllText(output, content);
                Console.Error.WriteLine($"Written to {output}");
            }
            else
            {
                Console.Write(content);
            }

            var gate = BadgeGate.Evaluate(metric, summary, failUnder, failOver);
            return gate.Outcome switch
            {
                GateOutcome.UsageError => Fail(gate.Message, ExitUsageError),
                GateOutcome.Fail => Fail(gate.Message, ExitGateFailed),
                _ => 0
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int Fail(string? message, int exitCode)
    {
        if (!string.IsNullOrEmpty(message))
            Console.Error.WriteLine(message);
        return exitCode;
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze badge - Output shields.io endpoint badge JSON

            Usage:
              unilyze badge                                    Analyze current directory
              unilyze badge -p <path>                          Analyze specified project
              unilyze badge -p <path> --metric codehealth      Code health badge (default)
              unilyze badge -p <path> --metric mi              Maintainability index badge
              unilyze badge -p <path> --metric smells          Code smells badge
              unilyze badge -p <path> -o badge.json            Write JSON to file
              unilyze badge -p <path> --format svg -o codehealth.svg   SVG badge (works in private repos via relative path)
              unilyze badge --metric codehealth --fail-under 7  Exit 2 if min CodeHealth < 7 (CI gate)
              unilyze badge --metric mi --fail-under 70         Exit 2 if average MI < 70
              unilyze badge --metric smells --fail-over 5       Exit 2 if warnings > 5 (or any critical)
              unilyze badge --projects 'packages/*' --metric codehealth --fail-under 7 -o out/
                                                           Per-project badges + summary table on stderr

            Options:
              -p, --path     Project root (default: .)
              --projects     Glob of project roots (repeatable; requires -o <dir> when multiple match)
              -o, --output   Output file path (default: stdout)
              --metric       Badge metric: codehealth, mi, smells (default: codehealth)
              --format       Output format: json, svg (default: json)
              --level        Pin analysis level: syntax, core, full, complete
              --fail-under   Quality gate for codehealth/mi: fail if value below threshold
                             (codehealth: min CodeHealth, mi: average MI)
              --fail-over    Quality gate for smells: fail if warning count above count
                             (any critical smell always fails)
              --baseline     Suppress known smells from a baseline file before gating
              -h, --help     Show this help

            Exit codes:
              0  Success / gate passed
              1  Usage error (e.g. --fail-under with --metric smells)
              2  Quality gate failed

            Output format (shields.io endpoint JSON or flat SVG):
              { "schemaVersion": 1, "label": "...", "message": "...", "color": "...", "analysisLevel": "..." }
              shields.io ignores the extra analysisLevel field.
              SVG: single-line shields.io flat badge (use --format svg);
                   the analysis level is embedded as a leading XML comment.
            """);
        return 0;
    }
}
