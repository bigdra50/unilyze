using System.Text.Json.Nodes;
using Unilyze;

namespace Unilyze.Tests.Pipeline;

public sealed class GoldenCorpusCompleteTests
{
    const string ValidationEnvVar = "UNILYZE_COMPLETE_VALIDATION";

    [Fact]
    public void GoldenFixture_WithUnityEditor_MatchesPinnedSemanticMetrics()
    {
        if (!IsValidationRequested())
            return;

        File.Delete(GoldenCorpusTestSupport.CsprojPath);
        Assert.False(File.Exists(GoldenCorpusTestSupport.CsprojPath));

        var editorsRoot = Environment.GetEnvironmentVariable("UNILYZE_EDITORS_ROOT")
            ?? throw new InvalidOperationException("UNILYZE_EDITORS_ROOT was not configured.");
        var unityVersion = ReadUnityVersion();
        Assert.True(
            Directory.Exists(Path.Combine(editorsRoot, unityVersion)),
            $"Unity Hub version directory was not found: {Path.Combine(editorsRoot, unityVersion)}");

        var resolved = UnityDllResolver.Resolve(GoldenCorpusTestSupport.FixtureRoot);
        Assert.Equal(AnalysisLevel.Complete, resolved.Level);
        Assert.Contains(
            resolved.Paths,
            path => path.Contains(
                $"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}ScriptAssemblies{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

        var (exitCode, stdout, stderr) = GoldenCorpusTestSupport.Run(
            "-p",
            GoldenCorpusTestSupport.FixtureRoot,
            "--level",
            "complete",
            "-f",
            "json");
        Assert.True(exitCode == 0, $"Complete analysis failed with exit code {exitCode}:{Environment.NewLine}{stderr}");

        var actualRoot = GoldenCorpusTestSupport.ParseNormalized(stdout);
        Assert.Equal("Complete", actualRoot["analysisLevel"]?.GetValue<string>());
        Assert.Equal("unity", actualRoot["projectKind"]?.GetValue<string>());

        var expectedRoot = JsonNode.Parse(File.ReadAllText(GoldenCorpusTestSupport.ExpectedPath))?.AsObject()
            ?? throw new InvalidOperationException("Failed to parse the pinned golden JSON.");
        expectedRoot["analysisLevel"] = "Complete";

        Assert.Equal(CreateMetricSnapshot(expectedRoot), CreateMetricSnapshot(actualRoot));
        Assert.Equal(
            GoldenCorpusTestSupport.Serialize(expectedRoot),
            GoldenCorpusTestSupport.Serialize(actualRoot));
    }

    static GoldenMetricSnapshot CreateMetricSnapshot(JsonObject root)
    {
        var types = root["types"]?.AsArray()
            ?? throw new InvalidOperationException("Golden JSON did not contain types.");
        var typeMetrics = root["typeMetrics"]?.AsArray()
            ?? throw new InvalidOperationException("Golden JSON did not contain typeMetrics.");

        var smellCount = typeMetrics
            .SelectMany(metric => metric?["codeSmells"]?.AsArray() ?? [])
            .Count();
        var boxingCount = typeMetrics
            .Sum(metric => metric?["boxingCount"]?.GetValue<int>() ?? 0);

        return new GoldenMetricSnapshot(types.Count, smellCount, boxingCount);
    }

    static bool IsValidationRequested()
        => string.Equals(
            Environment.GetEnvironmentVariable(ValidationEnvVar),
            "1",
            StringComparison.Ordinal);

    static string ReadUnityVersion()
    {
        var projectVersionPath = Path.Combine(
            GoldenCorpusTestSupport.FixtureRoot,
            "ProjectSettings",
            "ProjectVersion.txt");
        const string Prefix = "m_EditorVersion:";

        var version = File.ReadLines(projectVersionPath)
            .FirstOrDefault(line => line.StartsWith(Prefix, StringComparison.Ordinal))?[Prefix.Length..]
            .Trim();

        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidOperationException("m_EditorVersion was not found in ProjectVersion.txt.")
            : version;
    }

    sealed record GoldenMetricSnapshot(int TypeCount, int SmellCount, int BoxingCount);
}
