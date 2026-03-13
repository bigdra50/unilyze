using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze;

public sealed record CompilationResult(
    CSharpCompilation? Compilation,
    AnalysisLevel Level);

public static class CompilationFactory
{
    public static CompilationResult Create(
        ResolvedDlls resolved,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CsprojInfo? csprojInfo = null)
    {
        resolved = MergeWithCsprojReferences(resolved, csprojInfo);

        if (resolved.Level == AnalysisLevel.SyntaxOnly || resolved.Paths.Count == 0)
            return new CompilationResult(null, AnalysisLevel.SyntaxOnly);

        var (references, failedCount) = LoadReferences(resolved.Paths);

        if (references.Count == 0)
            return new CompilationResult(null, AnalysisLevel.SyntaxOnly);

        // Downgrade level if significant portion of references failed
        var level = resolved.Level;
        if (failedCount > 0)
        {
            var failRatio = (double)failedCount / resolved.Paths.Count;
            if (failRatio > 0.5)
            {
                Console.Error.WriteLine($"Warning: {failedCount}/{resolved.Paths.Count} references failed to load, downgrading to SyntaxOnly");
                return new CompilationResult(null, AnalysisLevel.SyntaxOnly);
            }
            Console.Error.WriteLine($"Warning: {failedCount}/{resolved.Paths.Count} references failed to load");
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
        var mergedLevel = resolved.Level == AnalysisLevel.SyntaxOnly && merged.Count > 0
            ? AnalysisLevel.CoreEngine
            : resolved.Level;
        return new ResolvedDlls(mergedLevel, merged.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static (List<MetadataReference> References, int FailedCount) LoadReferences(
        IReadOnlyList<string> paths)
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
                Console.Error.WriteLine($"Warning: Skipped {Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return (references, failedCount);
    }
}
