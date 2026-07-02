using Unilyze.Incremental;
using Unilyze.Tests.Helpers;

namespace Unilyze.Tests.Incremental;

// Fast, in-process coverage for SyntaxIncrementalCollector.ComputeFileUsingsHash: the
// normalization/sort step that makes the per-file using-directive hash order- and
// whitespace-insensitive (so reordering imports or reformatting never looks like a using
// change), while still being sensitive to an actual retarget. The end-to-end equivalence of
// this hash feeding HasStructuralChange is covered by SemanticIncrementalEquivalenceTests.
public sealed class SyntaxIncrementalCollectorUsingsHashTests
{
    [Fact]
    public void ReorderedUsings_ProduceTheSameHash()
    {
        var original = RoslynTestHelper.ParseCode("""
            using System;
            using System.Linq;

            namespace Sample;

            public class Foo { }
            """);
        var reordered = RoslynTestHelper.ParseCode("""
            using System.Linq;
            using System;

            namespace Sample;

            public class Foo { }
            """);

        Assert.Equal(
            SyntaxIncrementalCollector.ComputeFileUsingsHash(original),
            SyntaxIncrementalCollector.ComputeFileUsingsHash(reordered));
    }

    [Fact]
    public void WhitespaceOnlyChanges_ProduceTheSameHash()
    {
        var original = RoslynTestHelper.ParseCode("""
            using System;
            using   Sample.NsP1;

            namespace Sample;

            public class Foo { }
            """);
        var reformatted = RoslynTestHelper.ParseCode("""
            using    System;
            using Sample.NsP1;

            namespace Sample;

            public class Foo { }
            """);

        Assert.Equal(
            SyntaxIncrementalCollector.ComputeFileUsingsHash(original),
            SyntaxIncrementalCollector.ComputeFileUsingsHash(reformatted));
    }

    [Fact]
    public void RetargetedAlias_ProducesADifferentHash()
    {
        var before = RoslynTestHelper.ParseCode("""
            using A = Sample.Ns1.Bar;

            namespace Sample;

            public class Foo : A { }
            """);
        var after = RoslynTestHelper.ParseCode("""
            using A = Sample.Ns2.Qux;

            namespace Sample;

            public class Foo : A { }
            """);

        Assert.NotEqual(
            SyntaxIncrementalCollector.ComputeFileUsingsHash(before),
            SyntaxIncrementalCollector.ComputeFileUsingsHash(after));
    }

    [Fact]
    public void NamespaceScopedUsings_AreIncluded()
    {
        var withoutUsing = RoslynTestHelper.ParseCode("""
            namespace Sample
            {
                public class Foo { }
            }
            """);
        var withNamespaceScopedUsing = RoslynTestHelper.ParseCode("""
            namespace Sample
            {
                using System;

                public class Foo { }
            }
            """);

        Assert.NotEqual(
            SyntaxIncrementalCollector.ComputeFileUsingsHash(withoutUsing),
            SyntaxIncrementalCollector.ComputeFileUsingsHash(withNamespaceScopedUsing));
    }

    [Fact]
    public void GlobalUsings_AreExcluded()
    {
        // Global usings are already covered project-wide by the separate
        // GlobalUsingsHashesByAssembly check; folding them in here too would just produce a
        // duplicate, differently-worded full-re-enrich reason for the same root cause.
        var withoutUsing = RoslynTestHelper.ParseCode("""
            namespace Sample;

            public class Foo { }
            """);
        var withGlobalUsing = RoslynTestHelper.ParseCode("""
            global using System;

            namespace Sample;

            public class Foo { }
            """);

        Assert.Equal(
            SyntaxIncrementalCollector.ComputeFileUsingsHash(withoutUsing),
            SyntaxIncrementalCollector.ComputeFileUsingsHash(withGlobalUsing));
    }
}
