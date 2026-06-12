using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze;

internal sealed record PipelineDiscoverState(
    IReadOnlyList<AsmdefInfo> Targets,
    string ProjectRoot,
    string ProjectKind,
    CsprojInfo? CsprojInfo,
    IReadOnlyList<string> PreprocessorSymbols,
    ResolvedDlls ResolvedReferences,
    IReadOnlyList<string> CsprojFiles,
    string? SelectedTargetFramework);

internal sealed record PipelineCompileState(
    CompilationResult CompilationResult,
    string AnalysisLevel);

internal static class AnalysisPipelineDiscovery
{
    public static PipelineDiscoverState Discover(AnalysisBuildOptions options)
    {
        var projectRoot = ProgramHelpers.ResolveProjectRoot(options.Path);
        var projectKind = ProgramHelpers.ResolveProjectKind(projectRoot);
        var assetsDir = ProgramHelpers.ResolveAssetsDir(options.Path);
        var asmdefs = AsmdefInfo.Discover(
            assetsDir, options.ExcludeDirectories, options.ExcludeGeneratedCode, options.ApplyAnyDepthExcludes);

        IReadOnlyList<AsmdefInfo> targets;
        if (asmdefs.Count > 0)
        {
            var prefix = options.Prefix ?? ProgramHelpers.DetectCommonPrefix(asmdefs);
            targets = ProgramHelpers.FilterAssemblies(asmdefs, prefix, options.AssemblyFilter);
        }
        else if (projectKind != "unity")
        {
            var csprojAssemblies = CsprojAssemblyDiscovery.Discover(projectRoot, options.ExcludeDirectories);
            if (csprojAssemblies.Count > 0)
            {
                var prefix = options.Prefix ?? ProgramHelpers.DetectCommonPrefix(csprojAssemblies);
                targets = ProgramHelpers.FilterAssemblies(csprojAssemblies, prefix, options.AssemblyFilter);
            }
            else
            {
                targets = [new AsmdefInfo("Assembly-CSharp", assetsDir, [])];
            }
        }
        else
        {
            targets = [new AsmdefInfo("Assembly-CSharp", assetsDir, [])];
        }

        var csprojInfo = ResolveCsprojInfo(projectRoot, options.ExcludeDirectories, options.EffectiveLog);
        var csprojFiles = CsprojParser.DiscoverCsprojFiles(projectRoot, options.ExcludeDirectories);
        string? selectedTfm = null;
        if ((options.ResolveNuget || options.IncludeGenerated) && projectKind != "unity")
        {
            selectedTfm = TargetFrameworkSelector.ResolveForProject(
                csprojFiles, csprojInfo?.TargetFrameworks, options.TargetFramework);
        }

        var resolvedReferences = ResolveReferences(
            projectRoot, projectKind, csprojFiles, csprojInfo, options, selectedTfm, options.EffectiveLog);
        var preprocessorSymbols = MergePreprocessorSymbols(projectRoot, csprojInfo);

        return new PipelineDiscoverState(
            targets, projectRoot, projectKind, csprojInfo, preprocessorSymbols,
            resolvedReferences, csprojFiles, selectedTfm);
    }

    public static (List<TypeNodeInfo> Types, List<SyntaxTree> Trees) CollectTypes(
        PipelineDiscoverState discover, AnalysisBuildOptions options)
    {
        if (options.UseSyntaxIncrementalCache)
            return CollectTypesIncremental(discover, options);

        return CollectTypesFull(discover, options);
    }

    static (List<TypeNodeInfo> Types, List<SyntaxTree> Trees) CollectTypesFull(
        PipelineDiscoverState discover, AnalysisBuildOptions options)
    {
        var allTypes = new List<TypeNodeInfo>();
        var allTrees = new List<SyntaxTree>();
        foreach (var asm in discover.Targets)
        {
            var merged = MergeExcludeDirectories(asm.ExcludeDirectories, options.ExcludeDirectories);
            var result = TypeAnalyzer.AnalyzeDirectoryWithTrees(
                asm.Directory, asm.Name, discover.PreprocessorSymbols, merged,
                options.ExcludeGeneratedCode, options.ApplyAnyDepthExcludes,
                options.EffectiveMaxParallelism);
            allTypes.AddRange(result.Types);
            allTrees.AddRange(result.SyntaxTrees);
        }

        return (allTypes, allTrees);
    }

    static (List<TypeNodeInfo> Types, List<SyntaxTree> Trees) CollectTypesIncremental(
        PipelineDiscoverState discover, AnalysisBuildOptions options)
    {
        var collect = SyntaxIncrementalCollector.Collect(discover, options);
        SyntaxIncrementalState.Current = collect;
        return (collect.Types, collect.SyntaxTrees);
    }

    public static PipelineCompileState Compile(
        AnalysisBuildOptions options,
        PipelineDiscoverState discover,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        IReadOnlyList<SyntaxTree> referenceOnlyTrees,
        IAnalysisLogSink log)
    {
        var compilationTrees = referenceOnlyTrees.Count > 0
            ? syntaxTrees.Concat(referenceOnlyTrees).ToList()
            : syntaxTrees;

        var compilationResult = CompilationFactory.Create(
            discover.ResolvedReferences, compilationTrees, discover.CsprojInfo, options.EffectiveCap, log);
        var analysisLevel = AnalysisLevelOption.ToExternalName(compilationResult.Level, discover.ProjectKind);

        log.Info($"Analysis level: {analysisLevel}");
        LogLevelDegradationWarnings(discover.ProjectKind, compilationResult.Level, options.RequestedLevel, log);
        EnsureRequestedLevelSatisfied(options.RequestedLevel, compilationResult.Level, discover.ProjectKind);

        return new PipelineCompileState(compilationResult, analysisLevel);
    }

    public static IReadOnlyList<SyntaxTree> CollectReferenceOnlyTrees(
        PipelineDiscoverState discover,
        AnalysisBuildOptions options,
        IReadOnlyList<SyntaxTree> analysisTrees)
    {
        if (!options.IncludeGenerated || discover.ProjectKind == "unity")
            return [];

        var existingPaths = new HashSet<string>(
            analysisTrees
                .Select(t => Path.GetFullPath(t.FilePath ?? string.Empty))
                .Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        return GeneratedSourcesResolver.Collect(
            discover.CsprojFiles,
            discover.CsprojInfo?.TargetFrameworks,
            options.TargetFramework,
            discover.SelectedTargetFramework,
            existingPaths,
            discover.PreprocessorSymbols);
    }

    static ResolvedDlls ResolveReferences(
        string projectRoot,
        string projectKind,
        IReadOnlyList<string> csprojFiles,
        CsprojInfo? csprojInfo,
        AnalysisBuildOptions options,
        string? selectedTfm,
        IAnalysisLogSink log)
    {
        if (projectKind == "unity")
            return UnityDllResolver.Resolve(projectRoot, options.EffectiveCap);

        var resolved = DotnetRuntimeReferenceResolver.Resolve(options.EffectiveCap);
        if (!options.ResolveNuget)
            return resolved;

        var nugetPaths = NuGetAssetsReferenceResolver.Resolve(
            csprojFiles,
            csprojInfo?.TargetFrameworks,
            options.TargetFramework,
            selectedTfm,
            log);

        if (nugetPaths.Count == 0)
            return resolved;

        var merged = new List<string>(resolved.Paths);
        merged.AddRange(nugetPaths);
        return new ResolvedDlls(resolved.Level, merged.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    static void LogLevelDegradationWarnings(
        string projectKind, AnalysisLevel resolvedLevel, AnalysisLevel? requestedLevel, IAnalysisLogSink log)
    {
        if (resolvedLevel != AnalysisLevel.Syntax || requestedLevel is AnalysisLevel.Syntax)
            return;

        if (projectKind == "unity")
        {
            log.Warning(
                "Warning: Unity project detected but Unity DLLs could not be resolved; "
                + "analysis degraded to SyntaxOnly. Semantic metrics (boxing, CBO, DIT, etc.) are understated.");
            return;
        }

        log.Warning(
            "Warning: .NET runtime references could not be resolved; "
            + "analysis degraded to SyntaxOnly. Semantic metrics (boxing, CBO, DIT, etc.) are understated.");
    }

    static void EnsureRequestedLevelSatisfied(
        AnalysisLevel? requestedLevel, AnalysisLevel resolvedLevel, string projectKind)
    {
        if (requestedLevel is not { } required || resolvedLevel >= required)
            return;

        var hint = projectKind == "unity"
            ? "Unity DLLs may be missing."
            : ".NET runtime references may be unavailable.";
        throw new InvalidOperationException(
            "Requested analysis level '" + FormatAnalysisLevel(required) + "' could not be satisfied "
            + "(resolved '" + FormatAnalysisLevel(resolvedLevel) + "'). " + hint);
    }

    static string FormatAnalysisLevel(AnalysisLevel level) =>
        level switch
        {
            AnalysisLevel.Syntax => nameof(AnalysisLevel.Syntax),
            AnalysisLevel.Core => nameof(AnalysisLevel.Core),
            AnalysisLevel.Full => nameof(AnalysisLevel.Full),
            AnalysisLevel.Complete => nameof(AnalysisLevel.Complete),
            _ => "Unknown"
        };

    static CsprojInfo? ResolveCsprojInfo(
        string projectRoot, IReadOnlyList<string>? excludeDirectories, IAnalysisLogSink log)
    {
        var csprojFiles = CsprojParser.DiscoverCsprojFiles(projectRoot, excludeDirectories);
        if (csprojFiles.Count == 0)
            return null;

        var allRefs = new List<string>();
        var allProjectRefs = new List<string>();
        var allDefines = new List<string>();
        var allTfms = new List<string>();
        string? langVersion = null;
        foreach (var csproj in csprojFiles)
        {
            var info = CsprojParser.TryParse(csproj);
            if (info is null)
                continue;
            allRefs.AddRange(info.ReferencePaths);
            allProjectRefs.AddRange(info.ProjectReferences);
            allDefines.AddRange(info.DefineConstants);
            allTfms.AddRange(info.TargetFrameworks);
            langVersion ??= info.LangVersion;
        }

        if (allRefs.Count == 0 && allDefines.Count == 0 && langVersion is null && allTfms.Count == 0)
            return null;

        log.Info($"Found {csprojFiles.Count} .csproj file(s), {allRefs.Count} references, {allDefines.Count} defines");
        return new CsprojInfo(
            allRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            allProjectRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            allDefines.Distinct().ToList(),
            langVersion,
            allTfms.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    static IReadOnlyList<string> MergePreprocessorSymbols(string projectRoot, CsprojInfo? csprojInfo)
    {
        var symbols = UnityDllResolver.GetPreprocessorDefines(projectRoot);
        if (csprojInfo is not { DefineConstants.Count: > 0 })
            return symbols;

        var merged = new List<string>(symbols);
        merged.AddRange(csprojInfo.DefineConstants);
        return merged.Distinct().ToList();
    }

    static IReadOnlyList<string>? MergeExcludeDirectories(
        IReadOnlyList<string>? asmExclude, IReadOnlyList<string>? configExclude)
    {
        if (asmExclude is not { Count: > 0 } && configExclude is not { Count: > 0 })
            return null;
        if (asmExclude is not { Count: > 0 })
            return configExclude;
        if (configExclude is not { Count: > 0 })
            return asmExclude;

        var merged = new List<string>(asmExclude.Count + configExclude.Count);
        merged.AddRange(asmExclude);
        merged.AddRange(configExclude);
        return merged;
    }

    internal static IReadOnlyList<string>? MergeExcludeDirectoriesPublic(
        IReadOnlyList<string>? asmExclude, IReadOnlyList<string>? configExclude)
        => MergeExcludeDirectories(asmExclude, configExclude);
}
