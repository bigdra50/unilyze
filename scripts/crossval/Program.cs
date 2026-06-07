// Cross-validation harness for issue #4: re-measures cyclomatic complexity with the
// official Roslyn metrics engine (CodeAnalysisMetricData, the implementation behind
// Metrics.exe) and emits per-type / per-member data plus per-method counts of the
// constructs unilyze counts beyond CA1502 (?. / ?? / ??= / switch expression arms).
//
// Usage: dotnet run --project scripts/crossval -- <path-to-Unilyze.csproj>
// Output: JSON on stdout. Compare against `unilyze -f json` per-type method sums.

using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeMetrics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

MSBuildLocator.RegisterDefaults();

var projectPath = Path.GetFullPath(args[0]);

using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
{
    ["TargetFramework"] = "net10.0",
});
workspace.WorkspaceFailed += (_, e) =>
{
    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
        Console.Error.WriteLine($"workspace: {e.Diagnostic.Message}");
};

var project = await workspace.OpenProjectAsync(projectPath);
var compilation = await project.GetCompilationAsync()
    ?? throw new InvalidOperationException("compilation unavailable");

var context = new CodeMetricsAnalysisContext(compilation, CancellationToken.None);
var assemblyData = await CodeAnalysisMetricData.ComputeAsync(compilation.Assembly, context);

var types = new List<TypeReport>();
Walk(assemblyData);

void Walk(CodeAnalysisMetricData data)
{
    if (data.Symbol is INamedTypeSymbol type &&
        type.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface)
    {
        var members = data.Children
            .Select(c => new MemberReport(
                c.Symbol.Kind.ToString(),
                c.Symbol.MetadataName,
                c.CyclomaticComplexity))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        types.Add(new TypeReport(
            type.MetadataName,
            data.CyclomaticComplexity,
            members,
            CountExtendedConstructs(type)));
    }

    foreach (var child in data.Children)
        Walk(child);
}

List<MethodConstructs> CountExtendedConstructs(INamedTypeSymbol type)
{
    var reports = new List<MethodConstructs>();
    foreach (var syntaxRef in type.DeclaringSyntaxReferences)
    {
        if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax decl)
            continue;

        foreach (var method in decl.Members.OfType<MethodDeclarationSyntax>())
        {
            var nodes = method.DescendantNodes(n => n is not TypeDeclarationSyntax).ToList();
            var shared = nodes.Count(n => n is IfStatementSyntax or ConditionalExpressionSyntax
                    or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                    or ConditionalAccessExpressionSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax)
                + nodes.Count(n => n.IsKind(SyntaxKind.LogicalAndExpression)
                    || n.IsKind(SyntaxKind.LogicalOrExpression)
                    || n.IsKind(SyntaxKind.CoalesceExpression));

            reports.Add(new MethodConstructs(
                method.Identifier.Text,
                Shared: shared,
                DeconstructForeach: nodes.Count(n => n is ForEachVariableStatementSyntax),
                DefaultLabels: nodes.Count(n => n is DefaultSwitchLabelSyntax),
                Catches: nodes.Count(n => n is CatchClauseSyntax),
                SwitchArms: nodes.Count(n => n is SwitchExpressionArmSyntax),
                Gotos: nodes.Count(n => n is GotoStatementSyntax),
                BoolAmpOr: nodes.Count(n => n.IsKind(SyntaxKind.BitwiseAndExpression) || n.IsKind(SyntaxKind.BitwiseOrExpression))));
        }
    }

    return reports;
}

var report = types
    .Where(t => !t.Name.StartsWith("<", StringComparison.Ordinal))
    .OrderBy(t => t.Name, StringComparer.Ordinal)
    .ToList();

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

internal sealed record MethodConstructs(string Name, int Shared, int DeconstructForeach, int DefaultLabels, int Catches, int SwitchArms, int Gotos, int BoolAmpOr);

internal sealed record MemberReport(string Kind, string Name, int CycCC);

internal sealed record TypeReport(string Name, int OfficialCycCC, List<MemberReport> Members, List<MethodConstructs> Methods);
