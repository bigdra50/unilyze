using Unilyze.Discovery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Pipeline;

internal sealed record CompilationResult(
    CSharpCompilation? Compilation,
    AnalysisLevel Level);

internal static class CompilationFactory
{
    public static CompilationResult Create(
        ResolvedDlls resolved,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CsprojInfo? csprojInfo = null,
        AnalysisLevel maxLevel = AnalysisLevel.Complete)
        => Create(resolved, syntaxTrees, csprojInfo, maxLevel, logSink: null);

    internal static CompilationResult Create(
        ResolvedDlls resolved,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CsprojInfo? csprojInfo,
        AnalysisLevel maxLevel,
        IAnalysisLogSink? logSink)
    {
        var log = logSink ?? new ConsoleAnalysisLogSink(quiet: false);

        // A SyntaxOnly pin must not build a semantic model at all: the csproj
        // merge below would otherwise re-elevate SyntaxOnly to CoreEngine and
        // silently exceed the requested cap (issue 17).
        if (maxLevel == AnalysisLevel.Syntax)
            return new CompilationResult(null, AnalysisLevel.Syntax);

        resolved = MergeWithCsprojReferences(resolved, csprojInfo);

        if (resolved.Level == AnalysisLevel.Syntax || resolved.Paths.Count == 0)
            return new CompilationResult(null, AnalysisLevel.Syntax);

        var (references, failedCount) = LoadReferences(resolved.Paths, log);

        if (references.Count == 0)
            return new CompilationResult(null, AnalysisLevel.Syntax);

        // Downgrade level if significant portion of references failed
        var level = resolved.Level;
        if (failedCount > 0)
        {
            var failRatio = (double)failedCount / resolved.Paths.Count;
            if (failRatio > 0.5)
            {
                log.Warning($"Warning: {failedCount}/{resolved.Paths.Count} references failed to load, downgrading to Syntax");
                return new CompilationResult(null, AnalysisLevel.Syntax);
            }
            log.Warning($"Warning: {failedCount}/{resolved.Paths.Count} references failed to load");
        }

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            "UnilyzeAnalysis",
            syntaxTrees: syntaxTrees,
            references: references,
            options: options);

        return new CompilationResult(compilation, level);
    }

    private static ResolvedDlls MergeWithCsprojReferences(
        ResolvedDlls resolved,
        CsprojInfo? csprojInfo)
    {
        if (csprojInfo is not { ReferencePaths.Count: > 0 })
            return resolved;

        var merged = new List<string>(resolved.Paths);
        merged.AddRange(csprojInfo.ReferencePaths);
        var mergedLevel = resolved.Level == AnalysisLevel.Syntax && merged.Count > 0
            ? AnalysisLevel.Core
            : resolved.Level;
        return new ResolvedDlls(mergedLevel, merged.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static (List<MetadataReference> References, int FailedCount) LoadReferences(
        IReadOnlyList<string> paths,
        IAnalysisLogSink log)
    {
        var references = new List<MetadataReference>();
        var failedCount = 0;
        foreach (var path in paths)
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (Exception ex)
            {
                failedCount++;
                log.Warning($"Warning: Skipped {Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return (references, failedCount);
    }
}
