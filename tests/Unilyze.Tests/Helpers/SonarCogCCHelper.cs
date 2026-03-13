using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests.Helpers;

internal static class SonarCogCCHelper
{
    private static readonly string SonarAnalyzerDllPath =
        Path.Combine(AppContext.BaseDirectory, "SonarAnalyzer.CSharp.dll");

    /// <summary>
    /// Run SonarAnalyzer S3776 on a single code string.
    /// Returns method name -> CogCC score.
    /// </summary>
    public static async Task<Dictionary<string, int>> GetCognitiveComplexities(string code)
    {
        var results = await RunSonarBuild(
            new Dictionary<string, string> { ["Code.cs"] = code });
        return results.TryGetValue("Code.cs", out var methods)
            ? methods
            : new Dictionary<string, int>();
    }

    /// <summary>
    /// Run SonarAnalyzer S3776 on multiple source files.
    /// Returns fileName -> (methodName -> CogCC score).
    /// </summary>
    public static async Task<Dictionary<string, Dictionary<string, int>>> GetCognitiveComplexitiesMultiFile(
        Dictionary<string, string> sourceFiles)
    {
        return await RunSonarBuild(sourceFiles);
    }

    /// <summary>
    /// Run SonarAnalyzer S3776 on source file paths.
    /// Returns fileName -> (methodName -> CogCC score).
    /// </summary>
    public static async Task<Dictionary<string, Dictionary<string, int>>> GetCognitiveComplexitiesFromPaths(
        IEnumerable<string> filePaths, IEnumerable<string>? packageReferences = null)
    {
        var sourceFiles = new Dictionary<string, string>();
        foreach (var path in filePaths)
        {
            var fileName = Path.GetFileName(path);
            sourceFiles[fileName] = await File.ReadAllTextAsync(path);
        }
        return await RunSonarBuild(sourceFiles, packageReferences);
    }

    private static async Task<Dictionary<string, Dictionary<string, int>>> RunSonarBuild(
        Dictionary<string, string> sourceFiles, IEnumerable<string>? packageReferences = null)
    {
        if (!File.Exists(SonarAnalyzerDllPath))
            throw new FileNotFoundException($"SonarAnalyzer.CSharp.dll not found at {SonarAnalyzerDllPath}");

        var tempDir = Path.Combine(Path.GetTempPath(), $"sonar_cogcc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write source files
            foreach (var (fileName, content) in sourceFiles)
                await File.WriteAllTextAsync(Path.Combine(tempDir, fileName), content);

            // SonarLint.xml with threshold=0 to report all methods
            await File.WriteAllTextAsync(Path.Combine(tempDir, "SonarLint.xml"), """
                <AnalysisInput>
                  <Settings>
                    <Setting>
                      <Key>sonar.cs.analyzeGeneratedCode</Key>
                      <Value>true</Value>
                    </Setting>
                  </Settings>
                  <Rules>
                    <Rule>
                      <Key>S3776</Key>
                      <Parameters>
                        <Parameter><Key>threshold</Key><Value>0</Value></Parameter>
                      </Parameters>
                    </Rule>
                  </Rules>
                </AnalysisInput>
                """);

            // .globalconfig to enable S3776
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".globalconfig"),
                "is_global = true\ndotnet_diagnostic.S3776.severity = warning\n");

            // Build package references XML
            var pkgRefsXml = "";
            if (packageReferences != null)
            {
                var lines = packageReferences
                    .Select(pr => $"    <PackageReference Include=\"{pr.Split('/')[0]}\" Version=\"{pr.Split('/')[1]}\" />");
                pkgRefsXml = $"\n  <ItemGroup>\n{string.Join("\n", lines)}\n  </ItemGroup>";
            }

            // Create a minimal csproj
            await File.WriteAllTextAsync(Path.Combine(tempDir, "SonarTest.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutputType>Library</OutputType>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <WarningsAsErrors />
                    <NoWarn>CS8019;CS0168;CS0219;CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625;CS0162</NoWarn>
                  </PropertyGroup>
                  <ItemGroup>
                    <Analyzer Include="{SonarAnalyzerDllPath}" />
                    <AdditionalFiles Include="SonarLint.xml" />
                  </ItemGroup>{pkgRefsXml}
                </Project>
                """);

            // Restore + Build
            await RunDotnet(tempDir, "restore -v quiet");
            var output = await RunDotnet(tempDir, "build --no-restore -nologo -v quiet");

            // Parse S3776 diagnostics from build output
            // Format: "FileName.cs(line,col): warning S3776: ... from N to the 0 allowed."
            var diagRegex = new Regex(@"(\w+\.cs)\((\d+),(\d+)\).*warning S3776:.*from (\d+) to the");
            var results = new Dictionary<string, Dictionary<string, int>>();

            foreach (Match match in diagRegex.Matches(output))
            {
                var fileName = match.Groups[1].Value;
                var line = int.Parse(match.Groups[2].Value);
                var col = int.Parse(match.Groups[3].Value);
                var score = int.Parse(match.Groups[4].Value);

                if (!sourceFiles.TryGetValue(fileName, out var sourceCode))
                    continue;

                var tree = CSharpSyntaxTree.ParseText(sourceCode,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
                var root = tree.GetRoot();
                var lines = tree.GetText().Lines;

                if (line - 1 >= lines.Count) continue;
                var position = lines[line - 1].Start + col - 1;
                var node = root.FindToken(position).Parent;
                var methodName = ExtractMethodName(node);

                if (methodName != null)
                {
                    if (!results.ContainsKey(fileName))
                        results[fileName] = new Dictionary<string, int>();
                    results[fileName][methodName] = score;
                }
            }

            return results;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); }
            catch { /* best effort cleanup */ }
        }
    }

    private static async Task<string> RunDotnet(string workDir, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout + "\n" + stderr;
    }

    private static string? ExtractMethodName(SyntaxNode? node)
    {
        var current = node;
        while (current != null)
        {
            if (current is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method)
                return method.Identifier.Text;
            if (current is Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax ctor)
                return ctor.Identifier.Text;
            if (current is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax prop)
                return prop.Identifier.Text;
            current = current.Parent;
        }
        return null;
    }
}
