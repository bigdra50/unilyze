namespace Unilyze.Tests;

public class TypeAnalyzerTests : IDisposable
{
    readonly string _tempDir;

    public TypeAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_TypeAnalyzerTests_" + Path.GetRandomFileName());
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

    // --- 1. Empty directory ---

    [Fact]
    public void EmptyDirectory_ReturnsEmptyTypesAndTrees()
    {
        var result = TypeAnalyzer.AnalyzeDirectoryWithTrees(_tempDir, "TestAssembly");

        Assert.Empty(result.Types);
        Assert.Empty(result.SyntaxTrees);
    }

    // --- 2. Single class file ---

    [Fact]
    public void SingleClassFile_ExtractsTypeWithCorrectNameKindAssembly()
    {
        WriteFile("Foo.cs", """
            namespace Sample;
            public class Foo
            {
                public int Value { get; set; }
            }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "MyAssembly");

        var type = Assert.Single(result);
        Assert.Equal("Foo", type.Name);
        Assert.Equal("class", type.Kind);
        Assert.Equal("MyAssembly", type.Assembly);
    }

    // --- 3. Nested directory structure ---

    [Fact]
    public void NestedDirectories_DiscoversCsFilesRecursively()
    {
        WriteFile("A.cs", """
            public class A { }
            """);
        WriteFile("sub/B.cs", """
            public class B { }
            """);
        WriteFile("sub/deep/C.cs", """
            public class C { }
            """);

        var result = TypeAnalyzer.AnalyzeDirectoryWithTrees(_tempDir, "Asm");

        Assert.Equal(3, result.Types.Count);
        Assert.Equal(3, result.SyntaxTrees.Count);

        var names = result.Types.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["A", "B", "C"], names);
    }

    // --- 4. Namespace extraction ---

    [Fact]
    public void NamespaceExtraction_ReturnsCorrectNamespace()
    {
        WriteFile("Bar.cs", """
            namespace My.Deep.Namespace;
            public class Bar { }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var type = Assert.Single(result);
        Assert.Equal("My.Deep.Namespace", type.Namespace);
    }

    // --- 5. Base type and interface resolution ---

    [Fact]
    public void BaseTypeAndInterfaceResolution_SetsCorrectly()
    {
        WriteFile("Types.cs", """
            namespace Sample;

            public interface IService { }
            public class BaseEntity { }

            public class MyEntity : BaseEntity, IService { }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var myEntity = result.Single(t => t.Name == "MyEntity");

        Assert.Equal("BaseEntity", myEntity.BaseType);
        Assert.Contains("IService", myEntity.Interfaces);
    }

    [Fact]
    public void ClassNamedLikeInterface_RemainsBaseType()
    {
        WriteFile("Types.cs", """
            namespace Sample;

            public class IBuilder { }
            public class MyBuilder : IBuilder { }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var myBuilder = result.Single(t => t.Name == "MyBuilder");
        Assert.Equal("IBuilder", myBuilder.BaseType);
        Assert.Empty(myBuilder.Interfaces);
    }

    // --- 6. Partial type merging ---

    [Fact]
    public void PartialTypeMerging_TwoFiles_MergedIntoOneWithCombinedMembers()
    {
        WriteFile("PartialA.cs", """
            namespace Sample;
            public partial class Widget
            {
                public int X { get; set; }
            }
            """);
        WriteFile("PartialB.cs", """
            namespace Sample;
            public partial class Widget
            {
                public int Y { get; set; }
                public void DoWork() { }
            }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var widgets = result.Where(t => t.Name == "Widget").ToList();
        Assert.Single(widgets);

        var widget = widgets[0];
        var memberNames = widget.Members.Select(m => m.Name).OrderBy(n => n).ToList();
        Assert.Contains("X", memberNames);
        Assert.Contains("Y", memberNames);
        Assert.Contains("DoWork", memberNames);
    }

    [Fact]
    public void NestedTypes_WithSameSimpleName_GetDistinctTypeIds()
    {
        WriteFile("Nested.cs", """
            namespace Sample;

            public class OuterA
            {
                public class Inner { }
            }

            public class OuterB
            {
                public class Inner { }
            }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var innerTypes = result.Where(t => t.Name == "Inner").OrderBy(t => t.QualifiedName).ToList();
        Assert.Equal(2, innerTypes.Count);
        Assert.Equal("Sample.OuterA.Inner", innerTypes[0].QualifiedName);
        Assert.Equal("Asm::Sample.OuterA+Inner", innerTypes[0].TypeId);
        Assert.Equal("Sample.OuterB.Inner", innerTypes[1].QualifiedName);
        Assert.Equal("Asm::Sample.OuterB+Inner", innerTypes[1].TypeId);
    }

    [Fact]
    public void PartialNestedTypes_DoNotMergeAcrossDifferentParents()
    {
        WriteFile("OuterA.Part1.cs", """
            namespace Sample;
            public partial class OuterA
            {
                public partial class Inner
                {
                    public void A() { }
                }
            }
            """);
        WriteFile("OuterA.Part2.cs", """
            namespace Sample;
            public partial class OuterA
            {
                public partial class Inner
                {
                    public void B() { }
                }
            }
            """);
        WriteFile("OuterB.cs", """
            namespace Sample;
            public class OuterB
            {
                public partial class Inner
                {
                    public void C() { }
                }
            }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var innerTypes = result.Where(t => t.Name == "Inner").OrderBy(t => t.QualifiedName).ToList();
        Assert.Equal(2, innerTypes.Count);

        var outerAInner = Assert.Single(innerTypes.Where(t => t.QualifiedName == "Sample.OuterA.Inner"));
        Assert.Equal(["A", "B"], outerAInner.Members.Select(m => m.Name).OrderBy(n => n).ToList());

        var outerBInner = Assert.Single(innerTypes.Where(t => t.QualifiedName == "Sample.OuterB.Inner"));
        Assert.Equal(["C"], outerBInner.Members.Select(m => m.Name).OrderBy(n => n).ToList());
    }

    // --- 7. Enum extraction ---

    [Fact]
    public void EnumExtraction_KindIsEnumAndMembersExtracted()
    {
        WriteFile("Color.cs", """
            namespace Sample;
            public enum Color
            {
                Red,
                Green = 1,
                Blue = 2
            }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var color = result.Single(t => t.Name == "Color");
        Assert.Equal("enum", color.Kind);

        var memberNames = color.Members.Select(m => m.Name).ToList();
        Assert.Equal(3, memberNames.Count);
        Assert.Contains("Red", memberNames);
        Assert.Contains("Green", memberNames);
        Assert.Contains("Blue", memberNames);

        Assert.All(color.Members, m => Assert.Equal("EnumMember", m.MemberKind));
    }

    // --- 8. Delegate extraction ---

    [Fact]
    public void DelegateExtraction_KindIsDelegate()
    {
        WriteFile("Handler.cs", """
            namespace Sample;
            public delegate void EventHandler(object sender, int args);
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var handler = result.Single(t => t.Name == "EventHandler");
        Assert.Equal("delegate", handler.Kind);
        Assert.Equal("Sample", handler.Namespace);
    }

    // --- 9. Preprocessor symbols ---

    [Fact]
    public void PreprocessorSymbols_IncludesExcludesBasedOnSymbol()
    {
        WriteFile("Conditional.cs", """
            namespace Sample;
            public class Conditional
            {
            #if UNITY_EDITOR
                public void EditorOnly() { }
            #endif

            #if !UNITY_EDITOR
                public void RuntimeOnly() { }
            #endif
            }
            """);

        // With UNITY_EDITOR defined
        var withEditor = TypeAnalyzer.AnalyzeDirectoryWithTrees(
            _tempDir, "Asm", preprocessorSymbols: ["UNITY_EDITOR"]);
        var typeWith = withEditor.Types.Single(t => t.Name == "Conditional");
        var methodNamesWithEditor = typeWith.Members
            .Where(m => m.MemberKind == "Method")
            .Select(m => m.Name)
            .ToList();
        Assert.Contains("EditorOnly", methodNamesWithEditor);
        Assert.DoesNotContain("RuntimeOnly", methodNamesWithEditor);

        // Without UNITY_EDITOR defined
        var withoutEditor = TypeAnalyzer.AnalyzeDirectoryWithTrees(
            _tempDir, "Asm", preprocessorSymbols: null);
        var typeWithout = withoutEditor.Types.Single(t => t.Name == "Conditional");
        var methodNamesWithoutEditor = typeWithout.Members
            .Where(m => m.MemberKind == "Method")
            .Select(m => m.Name)
            .ToList();
        Assert.DoesNotContain("EditorOnly", methodNamesWithoutEditor);
        Assert.Contains("RuntimeOnly", methodNamesWithoutEditor);
    }

    // --- 10. Method metrics calculated ---

    [Fact]
    public void MethodMetrics_CogCCCycCCNestingDepthPopulated()
    {
        WriteFile("Metrics.cs", """
            namespace Sample;
            public class Metrics
            {
                public int Compute(int x)
                {
                    if (x > 0)          // CogCC +1, CycCC +1
                    {
                        if (x > 10)     // CogCC +2 (nesting=1), CycCC +1
                        {
                            return x;
                        }
                    }
                    return 0;
                }
            }
            """);

        var result = TypeAnalyzer.AnalyzeDirectory(_tempDir, "Asm");

        var metrics = result.Single(t => t.Name == "Metrics");
        var compute = metrics.Members.Single(m => m.Name == "Compute");

        Assert.Equal("Method", compute.MemberKind);
        Assert.NotNull(compute.CognitiveComplexity);
        Assert.NotNull(compute.CyclomaticComplexity);
        Assert.NotNull(compute.MaxNestingDepth);

        // if + nested if => CogCC = 1 + 2 = 3
        Assert.Equal(3, compute.CognitiveComplexity);
        // base 1 + 2 ifs => CycCC = 3
        Assert.Equal(3, compute.CyclomaticComplexity);
        // nested if inside if => max nesting = 2
        Assert.Equal(2, compute.MaxNestingDepth);
    }
}
