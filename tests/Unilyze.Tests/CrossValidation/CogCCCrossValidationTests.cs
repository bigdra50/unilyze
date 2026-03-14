using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unilyze.Tests.Helpers;
using Xunit.Abstractions;

namespace Unilyze.Tests.CrossValidation;

[Trait("Category", "CrossValidation")]
public class CogCCCrossValidationTests(ITestOutputHelper output)
{
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Unilyze"));

    [Fact]
    public async Task CrossValidate_UnilyzeSourceCode()
    {
        // Exclude Program.cs because top-level statements do not compile as a library in the Sonar helper project.
        var csFiles = Directory.GetFiles(SourceDir, "*.cs")
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return !name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        Assert.True(csFiles.Count > 0, $"No .cs files found in {SourceDir}");

        // 1. Calculate CogCC with Unilyze for each method
        var unilyzeMethods = new Dictionary<string, int>();
        foreach (var file in csFiles)
        {
            var code = await File.ReadAllTextAsync(file);
            var tree = CSharpSyntaxTree.ParseText(code,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
            var root = tree.GetRoot();
            var fileName = Path.GetFileName(file);

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                SyntaxNode? body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
                var score = CognitiveComplexity.Calculate(body);
                // Use parent type to disambiguate overloads
                var parentType = method.Parent is TypeDeclarationSyntax td ? td.Identifier.Text : "";
                var key = $"{fileName}:{parentType}.{method.Identifier.Text}";
                unilyzeMethods[key] = score;
            }

            foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                SyntaxNode? body = ctor.Body ?? (SyntaxNode?)ctor.ExpressionBody;
                var score = CognitiveComplexity.Calculate(body);
                var parentType = ctor.Parent is TypeDeclarationSyntax td ? td.Identifier.Text : "Unknown";
                var key = $"{fileName}:{parentType}.ctor";
                unilyzeMethods[key] = score;
            }
        }

        output.WriteLine($"Unilyze found {unilyzeMethods.Count} methods");
        output.WriteLine($"  Non-zero CogCC: {unilyzeMethods.Count(kv => kv.Value > 0)}");

        // 2. Run SonarAnalyzer on the same source files
        var sonarResults = await SonarCogCCHelper.GetCognitiveComplexitiesFromPaths(
            csFiles,
            [
                "Microsoft.CodeAnalysis.CSharp/4.12.0",
                "System.Text.Json/8.0.0",
            ]);

        // Flatten sonar results
        var sonarMethods = new Dictionary<string, int>();
        foreach (var (fileName, methods) in sonarResults)
        {
            foreach (var (methodName, score) in methods)
            {
                // SonarAnalyzer reports method name without parent type
                // Find matching Unilyze key by method name suffix
                var matchingKeys = unilyzeMethods.Keys
                    .Where(k => k.StartsWith($"{fileName}:") && k.EndsWith($".{methodName}"))
                    .ToList();
                foreach (var matchKey in matchingKeys)
                    sonarMethods[matchKey] = score;

                // Also try ctor match
                if (methodName.Length > 0 && char.IsUpper(methodName[0]))
                {
                    var ctorKeys = unilyzeMethods.Keys
                        .Where(k => k.StartsWith($"{fileName}:{methodName}.ctor"))
                        .ToList();
                    foreach (var ctorKey in ctorKeys)
                        sonarMethods[ctorKey] = score;
                }
            }
        }

        output.WriteLine($"SonarAnalyzer found {sonarMethods.Count} methods with CogCC > 0");

        // 3. Match and compare
        var matched = new List<(string Key, int Unilyze, int Sonar)>();

        // Methods found by both
        foreach (var key in unilyzeMethods.Keys.Intersect(sonarMethods.Keys))
            matched.Add((key, unilyzeMethods[key], sonarMethods[key]));

        // Methods only in Unilyze with score 0 => SonarAnalyzer agrees (no diagnostic = 0)
        foreach (var key in unilyzeMethods.Keys.Except(sonarMethods.Keys))
        {
            if (unilyzeMethods[key] == 0)
                matched.Add((key, 0, 0));
        }

        // Methods only in Unilyze with score > 0 => potential disagreement
        foreach (var key in unilyzeMethods.Keys.Except(sonarMethods.Keys))
        {
            if (unilyzeMethods[key] > 0)
                matched.Add((key, unilyzeMethods[key], 0));
        }

        Assert.True(matched.Count > 0,
            $"No methods could be matched. Unilyze: {unilyzeMethods.Count}, Sonar: {sonarMethods.Count}");

        // 4. Statistics
        var total = matched.Count;
        var exactMatch = matched.Count(m => m.Unilyze == m.Sonar);
        var within1 = matched.Count(m => Math.Abs(m.Unilyze - m.Sonar) <= 1);
        var exactMatchRate = (double)exactMatch / total;
        var within1Rate = (double)within1 / total;

        // Filter to methods with at least one non-zero score for Spearman
        var nonZeroMatched = matched.Where(m => m.Unilyze > 0 || m.Sonar > 0).ToList();
        var rho = nonZeroMatched.Count >= 2
            ? CalculateSpearmanRho(
                nonZeroMatched.Select(m => (double)m.Unilyze).ToArray(),
                nonZeroMatched.Select(m => (double)m.Sonar).ToArray())
            : 1.0; // Perfect correlation when no non-zero data

        // 5. Report
        var report = new System.Text.StringBuilder();
        report.AppendLine("CogCC Cross-Validation Report");
        report.AppendLine($"  Total methods: {total}");
        report.AppendLine($"  Non-zero methods: {nonZeroMatched.Count}");
        report.AppendLine($"  Exact match: {exactMatchRate:P1} ({exactMatch}/{total})");
        report.AppendLine($"  Within +-1: {within1Rate:P1} ({within1}/{total})");
        report.AppendLine($"  Spearman rho: {rho:F3}");

        var divergences = matched
            .Where(m => m.Unilyze != m.Sonar)
            .OrderByDescending(m => Math.Abs(m.Unilyze - m.Sonar))
            .ToList();

        if (divergences.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("  Divergences:");
            report.AppendLine("  Method                                       | Unilyze | Sonar | Delta");
            report.AppendLine("  ---------------------------------------------|---------|-------|------");
            foreach (var d in divergences)
            {
                var name = d.Key.Length > 45 ? d.Key[..45] : d.Key.PadRight(45);
                report.AppendLine($"  {name}| {d.Unilyze,7} | {d.Sonar,5} | {d.Unilyze - d.Sonar,5}");
            }
        }

        output.WriteLine(report.ToString());

        // Assertions
        Assert.True(rho >= 0.9,
            $"Spearman rho ({rho:F3}) should be >= 0.9\n{report}");
        Assert.True(exactMatchRate >= 0.5,
            $"Exact match rate ({exactMatchRate:P1}) should be >= 50%\n{report}");
        Assert.True(within1Rate >= 0.8,
            $"Within +-1 rate ({within1Rate:P1}) should be >= 80%\n{report}");
    }

    private static double CalculateSpearmanRho(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2) return 0;

        var n = x.Length;
        var rankX = GetRanks(x);
        var rankY = GetRanks(y);

        var meanRankX = rankX.Average();
        var meanRankY = rankY.Average();

        double numerator = 0, denomX = 0, denomY = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = rankX[i] - meanRankX;
            var dy = rankY[i] - meanRankY;
            numerator += dx * dy;
            denomX += dx * dx;
            denomY += dy * dy;
        }

        var denom = Math.Sqrt(denomX * denomY);
        return denom == 0 ? 1.0 : numerator / denom;
    }

    private static double[] GetRanks(double[] values)
    {
        var n = values.Length;
        var indexed = values
            .Select((v, i) => (Value: v, Index: i))
            .OrderBy(x => x.Value)
            .ToArray();

        var ranks = new double[n];
        var i = 0;
        while (i < n)
        {
            var j = i;
            while (j < n && indexed[j].Value == indexed[i].Value)
                j++;

            var avgRank = (i + j - 1) / 2.0 + 1;
            for (var k = i; k < j; k++)
                ranks[indexed[k].Index] = avgRank;

            i = j;
        }

        return ranks;
    }
}
