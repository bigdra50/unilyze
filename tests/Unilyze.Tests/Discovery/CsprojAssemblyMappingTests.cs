namespace Unilyze.Tests.Discovery;

public sealed class CsprojAssemblyMappingTests : IDisposable
{
    readonly string _tempDir;

    public CsprojAssemblyMappingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_CsprojAssembly_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    void WriteCsproj(string relativeDir, string name, params string[] projectReferences)
    {
        var refs = projectReferences.Length == 0
            ? string.Empty
            : string.Join('\n', projectReferences.Select(r => $"    <ProjectReference Include=\"{r}\" />"));
        var itemGroup = refs.Length == 0
            ? string.Empty
            : $"""
              <ItemGroup>
              {refs}
              </ItemGroup>
              """;
        WriteFile(Path.Combine(relativeDir, $"{name}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
            {itemGroup}
            </Project>
            """);
    }

    [Fact]
    public void Discover_BuildsOneAssemblyPerCsprojWithProjectReferences()
    {
        WriteCsproj("AppC", "AppC");
        WriteFile("AppC/CType.cs", "namespace AppC; public class CType { }");

        WriteCsproj("AppB", "AppB", @"..\AppC\AppC.csproj");
        WriteFile("AppB/BType.cs", """
            namespace AppB;
            using AppC;
            public class BType { public CType Field = new(); }
            """);

        WriteCsproj("AppA", "AppA", @"..\AppB\AppB.csproj");
        WriteFile("AppA/AType.cs", """
            namespace AppA;
            using AppB;
            public class AType { public BType Field = new(); }
            """);

        var discovered = CsprojAssemblyDiscovery.Discover(_tempDir);

        Assert.Equal(3, discovered.Count);
        Assert.Contains(discovered, a => a.Name == "AppA" && a.References.SequenceEqual(["AppB"]));
        Assert.Contains(discovered, a => a.Name == "AppB" && a.References.SequenceEqual(["AppC"]));
        Assert.Contains(discovered, a => a.Name == "AppC" && a.References.Count == 0);
    }

    [Fact]
    public void Discover_NestedCsproj_ExcludesChildDirectoryFromParent()
    {
        WriteCsproj("Parent", "Parent");
        WriteFile("Parent/ParentType.cs", "namespace Parent; public class ParentType { }");

        WriteCsproj("Parent/Nested", "Nested");
        WriteFile("Parent/Nested/NestedType.cs", "namespace Parent.Nested; public class NestedType { }");

        var discovered = CsprojAssemblyDiscovery.Discover(_tempDir);
        var parent = Assert.Single(discovered, a => a.Name == "Parent");
        var nested = Assert.Single(discovered, a => a.Name == "Nested");

        Assert.Contains(Path.GetFullPath(Path.Combine(_tempDir, "Parent", "Nested")), parent.ExcludeDirectories!);

        var parentTypes = TypeAnalyzer.AnalyzeDirectoryWithTrees(
            parent.Directory, parent.Name, excludeDirectories: parent.ExcludeDirectories).Types;
        var nestedTypes = TypeAnalyzer.AnalyzeDirectoryWithTrees(
            nested.Directory, nested.Name, excludeDirectories: nested.ExcludeDirectories).Types;

        Assert.Single(parentTypes);
        Assert.Equal("ParentType", parentTypes[0].Name);
        Assert.Single(nestedTypes);
        Assert.Equal("NestedType", nestedTypes[0].Name);
        Assert.Equal(["NestedType", "ParentType"], parentTypes.Concat(nestedTypes).Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void Discover_LooseFiles_AddsAssemblyCSharpFallback()
    {
        WriteCsproj("Lib", "Lib");
        WriteFile("Lib/LibType.cs", "namespace Lib; public class LibType { }");
        WriteFile("LooseType.cs", "namespace Root; public class LooseType { }");

        var discovered = CsprojAssemblyDiscovery.Discover(_tempDir);

        var lib = Assert.Single(discovered, a => a.Name == "Lib");
        var loose = Assert.Single(discovered, a => a.Name == "Assembly-CSharp");

        Assert.Equal(_tempDir, loose.Directory);
        Assert.Contains("Lib", loose.References);
        Assert.Contains(Path.GetFullPath(Path.Combine(_tempDir, "Lib")), loose.ExcludeDirectories!);

        var libTypes = TypeAnalyzer.AnalyzeDirectoryWithTrees(
            lib.Directory, lib.Name, excludeDirectories: lib.ExcludeDirectories).Types;
        var looseTypes = TypeAnalyzer.AnalyzeDirectoryWithTrees(
            loose.Directory, loose.Name, excludeDirectories: loose.ExcludeDirectories).Types;

        Assert.Equal("LibType", Assert.Single(libTypes).Name);
        Assert.Equal("LooseType", Assert.Single(looseTypes).Name);
    }

    [Fact]
    public void Discover_UnresolvedProjectReference_IsRecorded()
    {
        WriteCsproj("App", "App", @"..\Missing\Missing.csproj");
        WriteFile("App/AppType.cs", "namespace App; public class AppType { }");

        var discovered = CsprojAssemblyDiscovery.Discover(_tempDir);
        var app = Assert.Single(discovered);

        Assert.Empty(app.References);
        Assert.Contains(@"..\Missing\Missing.csproj", app.UnresolvedReferences!);
    }

    [Fact]
    public void Build_MultiCsprojSolution_ReportsPerAssemblyMetricsAndCycles()
    {
        WriteCsproj("AppC", "AppC");
        WriteFile("AppC/CType.cs", "namespace AppC; public class CType { }");

        WriteCsproj("AppB", "AppB", @"..\AppC\AppC.csproj");
        WriteFile("AppB/BType.cs", """
            namespace AppB;
            using AppC;
            public class BType { public CType Field = new(); }
            """);

        WriteCsproj("AppA", "AppA", @"..\AppB\AppB.csproj");
        WriteFile("AppA/AType.cs", """
            namespace AppA;
            using AppB;
            public class AType { public BType Field = new(); }
            """);

        WriteCsproj("MutualX", "MutualX", @"..\MutualY\MutualY.csproj");
        WriteFile("MutualX/XType.cs", """
            namespace MutualX;
            using MutualY;
            public class XType { public YType Field = new(); }
            """);

        WriteCsproj("MutualY", "MutualY", @"..\MutualX\MutualX.csproj");
        WriteFile("MutualY/YType.cs", """
            namespace MutualY;
            using MutualX;
            public class YType { public XType Field = new(); }
            """);

        WriteCsproj("Parent", "Parent");
        WriteFile("Parent/ParentType.cs", "namespace Parent; public class ParentType { }");
        WriteCsproj("Parent/Nested", "Nested");
        WriteFile("Parent/Nested/NestedType.cs", "namespace Parent.Nested; public class NestedType { }");

        WriteFile("LooseType.cs", "namespace Root; public class LooseType { }");

        var result = AnalysisPipeline.Build(_tempDir, null, null);

        Assert.Equal(8, result.Assemblies.Count);
        Assert.Contains(result.Assemblies, a => a.Name == "AppA" && a.References.Contains("AppB"));
        Assert.Contains(result.Assemblies, a => a.Name == "AppB" && a.References.Contains("AppC"));
        Assert.Contains(result.Assemblies, a => a.Name == "Assembly-CSharp");

        var assemblyNames = result.Assemblies.Select(a => a.Name).ToHashSet();
        Assert.All(result.Types, t => Assert.Contains(t.Assembly, assemblyNames));
        Assert.Equal(result.Types.Count, result.Types.Select(t => t.FilePath).Distinct().Count());

        var perAssemblyMetrics = result.Assemblies
            .Where(a => a.Name is "AppA" or "AppB" or "AppC")
            .ToDictionary(a => a.Name, a => a.Metrics);
        Assert.Equal(3, perAssemblyMetrics.Count);
        Assert.All(perAssemblyMetrics.Values, m => Assert.NotNull(m.DistanceFromMainSequence));
        Assert.Equal(3, perAssemblyMetrics.Values.Select(m => m.DistanceFromMainSequence).Distinct().Count());

        var assemblyCycle = Assert.Single(
            result.CyclicDependencies ?? [],
            c => c.Level == CycleLevel.Assembly);
        Assert.Contains("MutualX", assemblyCycle.Cycle);
        Assert.Contains("MutualY", assemblyCycle.Cycle);
    }

    [Fact]
    public void Build_AssemblyFilter_AppliesToCsprojDerivedAssemblies()
    {
        WriteCsproj("Prefix.One", "Prefix.One");
        WriteFile("Prefix.One/OneType.cs", "namespace Prefix.One; public class OneType { }");
        WriteCsproj("Prefix.Two", "Prefix.Two");
        WriteFile("Prefix.Two/TwoType.cs", "namespace Prefix.Two; public class TwoType { }");
        WriteCsproj("Other", "Other");
        WriteFile("Other/OtherType.cs", "namespace Other; public class OtherType { }");

        var filtered = AnalysisPipeline.Build(_tempDir, prefix: "Prefix.", assemblyFilter: null);
        Assert.Equal(2, filtered.Assemblies.Count);
        Assert.All(filtered.Assemblies, a => Assert.StartsWith("Prefix.", a.Name));

        var single = AnalysisPipeline.Build(_tempDir, prefix: null, assemblyFilter: "Other");
        Assert.Single(single.Assemblies);
        Assert.Equal("Other", single.Assemblies[0].Name);
    }
}
