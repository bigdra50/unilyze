namespace Unilyze.Tests;

public sealed class GoldenCorpusTests
{
    const string UpdateEnvVar = "UNILYZE_GOLDEN_UPDATE";

    [Fact]
    public void GoldenFixture_MatchesPinnedMetricsJson()
    {
        EnsureCsprojWithCoreEngineReference();

        var (exitCode, stdout, _) = GoldenCorpusTestSupport.Run(
            "-p",
            GoldenCorpusTestSupport.FixtureRoot,
            "-f",
            "json");
        Assert.Equal(0, exitCode);

        var actual = GoldenCorpusTestSupport.NormalizeForComparison(stdout);
        var root = GoldenCorpusTestSupport.ParseNormalized(actual);
        Assert.Equal("CoreEngine", root["analysisLevel"]?.GetValue<string>());
        Assert.Equal("unity", root["projectKind"]?.GetValue<string>());
        Assert.Equal(3.5, root["energyPressure"]?.GetValue<double>());

        var weakTemporization = root["typeMetrics"]!.AsArray()
            .Single(type => type!["typeName"]!.GetValue<string>() == "WeakTemporizationTarget")!;
        Assert.Equal(1, weakTemporization["hotPathMethodCount"]?.GetValue<int>());
        Assert.True(weakTemporization["codeSmells"]![0]!["inHotPath"]?.GetValue<bool>());

        if (IsUpdateRequested())
        {
            File.WriteAllText(GoldenCorpusTestSupport.ExpectedPath, actual);
            return;
        }

        Assert.True(File.Exists(GoldenCorpusTestSupport.ExpectedPath),
            $"Missing {GoldenCorpusTestSupport.ExpectedPath}. Regenerate with: UNILYZE_GOLDEN_UPDATE=1 dotnet test tests/Unilyze.Tests -f {GoldenCorpusTestSupport.CurrentTargetFramework} --filter GoldenCorpus");

        var expected = File.ReadAllText(GoldenCorpusTestSupport.ExpectedPath);
        Assert.Equal(expected, actual);
    }

    static void EnsureCsprojWithCoreEngineReference()
    {
        // Unity golden still needs a resolvable reference when the editor is absent in CI.
        var dllPath = typeof(object).Assembly.Location;
        File.WriteAllText(GoldenCorpusTestSupport.CsprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Reference Include="CoreLib">
                  <HintPath>{dllPath}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);
    }

    static bool IsUpdateRequested()
        => string.Equals(Environment.GetEnvironmentVariable(UpdateEnvVar), "1", StringComparison.Ordinal);
}
