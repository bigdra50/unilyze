using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Discovery;

internal static class GeneratedSourcesResolver
{
    public static IReadOnlyList<SyntaxTree> Collect(
        IReadOnlyList<string> csprojFiles,
        IReadOnlyList<string>? csprojTargetFrameworks,
        string? explicitTfm,
        string? selectedTfm,
        IReadOnlySet<string> existingSourcePaths,
        IReadOnlyList<string> preprocessorSymbols)
    {
        var tfm = selectedTfm ?? TargetFrameworkSelector.ResolveForProject(csprojFiles, csprojTargetFrameworks, explicitTfm);
        if (tfm is null)
            return [];

        var generatedDir = SelectGeneratedDirectory(csprojFiles, tfm);
        if (generatedDir is null || !Directory.Exists(generatedDir))
            return [];

        var parseOptions = BuildParseOptions(preprocessorSymbols);
        var trees = new List<SyntaxTree>();
        foreach (var file in Directory.EnumerateFiles(generatedDir, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = Path.GetFullPath(file);
            if (existingSourcePaths.Contains(normalized))
                continue;

            var source = File.ReadAllText(file);
            trees.Add(CSharpSyntaxTree.ParseText(source, options: parseOptions, path: file));
        }

        return trees
            .OrderBy(t => t.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    static CSharpParseOptions BuildParseOptions(IReadOnlyList<string> preprocessorSymbols)
    {
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        return preprocessorSymbols is { Count: > 0 }
            ? options.WithPreprocessorSymbols(preprocessorSymbols)
            : options;
    }

    static IEnumerable<string> DiscoverTargetFrameworks(string csprojDir)
    {
        var objDir = Path.Combine(csprojDir, "obj");
        if (!Directory.Exists(objDir))
            yield break;

        foreach (var configDir in Directory.EnumerateDirectories(objDir))
        {
            foreach (var tfmDir in Directory.EnumerateDirectories(configDir))
            {
                var generated = Path.Combine(tfmDir, "generated");
                if (Directory.Exists(generated))
                    yield return Path.GetFileName(tfmDir);
            }
        }
    }

    static string? SelectGeneratedDirectory(IReadOnlyList<string> csprojFiles, string targetFramework)
    {
        GeneratedDirectoryCandidate? best = null;
        foreach (var csproj in csprojFiles)
        {
            var csprojDir = Path.GetDirectoryName(Path.GetFullPath(csproj)) ?? ".";
            var objDir = Path.Combine(csprojDir, "obj");
            if (!Directory.Exists(objDir))
                continue;

            foreach (var configDir in Directory.EnumerateDirectories(objDir))
            {
                var generated = Path.Combine(configDir, targetFramework, "generated");
                if (!Directory.Exists(generated))
                    continue;

                var candidate = new GeneratedDirectoryCandidate(
                    generated,
                    Path.GetFileName(configDir),
                    Directory.GetLastWriteTimeUtc(generated));

                if (best is null || candidate.IsBetterThan(best))
                    best = candidate;
            }
        }

        return best?.Path;
    }

    sealed record GeneratedDirectoryCandidate(string Path, string Configuration, DateTime LastWriteUtc)
    {
        public bool IsBetterThan(GeneratedDirectoryCandidate other)
        {
            if (LastWriteUtc != other.LastWriteUtc)
                return LastWriteUtc > other.LastWriteUtc;

            var thisIsDebug = Configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase);
            var otherIsDebug = other.Configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase);
            if (thisIsDebug != otherIsDebug)
                return thisIsDebug;

            return string.Compare(Path, other.Path, StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
