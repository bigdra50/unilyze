using Unilyze.Incremental;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests.Incremental;

// UsageRecorder is the IOperation walk that produces UsedTypes(T) (design doc §4.1). These tests
// compile small fixtures for real (so bound symbols are genuine, not guessed) and assert the
// specific hazard-list surfaces the design's LLM debate (§9) called out: bound-symbol containing
// types, implicit conversions, operators, foreach/indexer patterns, extension methods, using
// static/alias targets, metadata exclusion, and partial-type TypeId convergence.
public sealed class UsageRecorderTests
{
    const string TestAssembly = "TestAssembly";

    [Fact]
    public void InvocationTarget_RecordsCalleeContainingType()
    {
        var used = RecordHost("""
            namespace Sample;

            public class Callee
            {
                public void DoWork() { }
            }

            public class Host
            {
                public void Run()
                {
                    var c = new Callee();
                    c.DoWork();
                }
            }
            """);

        Assert.Contains(TypeId("Callee"), used);
    }

    [Fact]
    public void PropertyReference_RecordsReceiverStaticType()
    {
        var used = RecordHost("""
            namespace Sample;

            public class Callee
            {
                public int Value { get; set; }
            }

            public class Host
            {
                public int Run(Callee c) => c.Value;
            }
            """);

        Assert.Contains(TypeId("Callee"), used);
    }

    [Fact]
    public void ImplicitConversion_RecordsOperatorContainingTypeAtArgumentPosition()
    {
        var used = RecordHost("""
            namespace Sample;

            public class ConversionSource
            {
                public static implicit operator int(ConversionSource s) => 0;
            }

            public class Host
            {
                void TakesInt(int x) { }

                public void Run()
                {
                    TakesInt(new ConversionSource());
                }
            }
            """);

        Assert.Contains(TypeId("ConversionSource"), used);
    }

    [Fact]
    public void BinaryOperator_RecordsOperatorMethodContainingType()
    {
        var used = RecordHost("""
            namespace Sample;

            public class Money
            {
                public static Money operator +(Money a, Money b) => a;
            }

            public class Host
            {
                public Money Run(Money a, Money b) => a + b;
            }
            """);

        Assert.Contains(TypeId("Money"), used);
    }

    [Fact]
    public void ForEach_RecordsEnumeratorPatternType()
    {
        var used = RecordHost("""
            using System.Collections;
            using System.Collections.Generic;

            namespace Sample;

            public class CustomSequence
            {
                public IEnumerator<int> GetEnumerator() => new List<int> { 1, 2 }.GetEnumerator();
            }

            public class Host
            {
                public void Run()
                {
                    foreach (var x in new CustomSequence()) { }
                }
            }
            """);

        Assert.Contains(TypeId("CustomSequence"), used);
    }

    [Fact]
    public void Indexer_RecordsReceiverContainingType()
    {
        var used = RecordHost("""
            namespace Sample;

            public class Grid
            {
                public int this[int i] => i;
            }

            public class Host
            {
                public int Run(Grid g) => g[0];
            }
            """);

        Assert.Contains(TypeId("Grid"), used);
    }

    [Fact]
    public void BaseChain_RecordsEveryAncestorNotJustImmediateBase()
    {
        var used = RecordHost("""
            namespace Sample;

            public class Root { }
            public class Middle : Root { }

            public class Host : Middle
            {
            }
            """);

        Assert.Contains(TypeId("Root"), used);
        Assert.Contains(TypeId("Middle"), used);
    }

    [Fact]
    public void Attribute_RecordsAttributeClassType()
    {
        var used = RecordHost("""
            using System;

            namespace Sample;

            [AttributeUsage(AttributeTargets.Class)]
            public class MarkerAttribute : Attribute { }

            [Marker]
            public class Host
            {
            }
            """);

        Assert.Contains(TypeId("MarkerAttribute"), used);
    }

    [Fact]
    public void ExtensionMethod_RecordsDeclaringStaticClassNotReceiverType()
    {
        var used = RecordHost("""
            namespace Sample;

            public class Widget
            {
                public int Value;
            }

            public static class Extensions
            {
                public static int Doubled(this Widget w) => w.Value * 2;
            }

            public class Host
            {
                public int Run(Widget w) => w.Doubled();
            }
            """);

        Assert.Contains(TypeId("Extensions"), used);
    }

    [Fact]
    public void UsingStaticTarget_IsRecordedForEveryTypeInTheFile()
    {
        var used = RecordHost("""
            using static Sample.StaticHelper;

            namespace Sample;

            public static class StaticHelper
            {
                public static void Helper() { }
            }

            public class Host
            {
                public void Run() => Helper();
            }
            """);

        Assert.Contains(TypeId("StaticHelper"), used);
    }

    [Fact]
    public void UsingAliasTarget_IsRecordedForEveryTypeInTheFile()
    {
        var used = RecordHost("""
            using A = Sample.AliasTarget;

            namespace Sample;

            public class AliasTarget { }

            public class Host : A
            {
            }
            """);

        Assert.Contains(TypeId("AliasTarget"), used);
    }

    [Fact]
    public void MetadataType_IsNotRecorded()
    {
        var used = RecordHost("""
            using System.Collections.Generic;

            namespace Sample;

            public class Host
            {
                public void Run()
                {
                    var list = new List<int>();
                    list.Add(1);
                }
            }
            """);

        Assert.DoesNotContain(used, id => id.Contains("List", StringComparison.Ordinal));
        Assert.DoesNotContain(used, id => id.StartsWith("System.", StringComparison.Ordinal)
            || id.Contains("::System.", StringComparison.Ordinal));
    }

    [Fact]
    public void PartialType_ConvergesToTheSameTypeIdFromEitherFragment()
    {
        var refs = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var pathA = Path.GetFullPath("/src/A.cs");
        var pathB = Path.GetFullPath("/src/B.cs");
        var treeA = CSharpSyntaxTree.ParseText(
            "namespace Sample;\npublic partial class Foo { public void M1() { } }\n",
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), pathA);
        var treeB = CSharpSyntaxTree.ParseText(
            "namespace Sample;\npublic partial class Foo { public void M2() { } }\n",
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), pathB);
        var compilation = CSharpCompilation.Create("Test", [treeA, treeB], refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var assemblyByFilePath = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [pathA] = TestAssembly,
            [pathB] = TestAssembly,
        };

        var typeDeclA = (TypeDeclarationSyntax)treeA.GetRoot().DescendantNodes()
            .First(n => n is TypeDeclarationSyntax);
        var typeDeclB = (TypeDeclarationSyntax)treeB.GetRoot().DescendantNodes()
            .First(n => n is TypeDeclarationSyntax);

        var symbolFromA = compilation.GetSemanticModel(treeA).GetDeclaredSymbol(typeDeclA) as INamedTypeSymbol;
        var symbolFromB = compilation.GetSemanticModel(treeB).GetDeclaredSymbol(typeDeclB) as INamedTypeSymbol;

        Assert.NotNull(symbolFromA);
        Assert.Equal(2, symbolFromA!.DeclaringSyntaxReferences.Length);

        var typeIdFromA = UsageRecorder.TryResolveTypeId(symbolFromA, assemblyByFilePath);
        var typeIdFromB = UsageRecorder.TryResolveTypeId(symbolFromB!, assemblyByFilePath);

        Assert.NotNull(typeIdFromA);
        Assert.Equal(typeIdFromA, typeIdFromB);
    }

    // Discriminating fixture: the extension class hosting GetEnumerator is reachable ONLY via
    // ForEachStatementInfo (never as an operation type or child invocation), so this fails if
    // deconstructing foreach loops (ForEachVariableStatementSyntax) skip the pattern recording.
    [Fact]
    public void DeconstructingForEach_RecordsExtensionEnumeratorClass()
    {
        var used = RecordHost("""
            using System.Collections.Generic;

            namespace Sample;

            public class PairSequence { }

            public static class SeqExtensions
            {
                public static IEnumerator<(int, int)> GetEnumerator(this PairSequence s) =>
                    new List<(int, int)> { (1, 2) }.GetEnumerator();
            }

            public class Host
            {
                public void Run(PairSequence seq)
                {
                    foreach (var (a, b) in seq) { }
                }
            }
            """);

        Assert.Contains(TypeId("SeqExtensions"), used);
    }

    // Discriminating fixture: an extension Deconstruct never appears as a child
    // IInvocationOperation — only DeconstructionInfo.Method carries it.
    [Fact]
    public void DeconstructionAssignment_RecordsExtensionDeconstructClass()
    {
        var used = RecordHost("""
            namespace Sample;

            public class Point
            {
                public int X { get; set; }
                public int Y { get; set; }
            }

            public static class PointExtensions
            {
                public static void Deconstruct(this Point p, out int x, out int y)
                {
                    x = p.X;
                    y = p.Y;
                }
            }

            public class Host
            {
                public void Run(Point p)
                {
                    var (x, y) = p;
                }
            }
            """);

        Assert.Contains(TypeId("PointExtensions"), used);
    }

    static string TypeId(string simpleName) => $"{TestAssembly}::Sample.{simpleName}";

    static IReadOnlyList<string> RecordHost(string code)
    {
        var path = Path.GetFullPath("/src/Fixture.cs");
        var tree = CSharpSyntaxTree.ParseText(
            code, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), path);
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "Test", [tree], refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        var hostDecl = tree.GetRoot().DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(t => t.Identifier.Text == "Host");

        var assemblyByFilePath = new Dictionary<string, string>(StringComparer.Ordinal) { [path] = TestAssembly };
        return UsageRecorder.Record(hostDecl, model, assemblyByFilePath);
    }
}
