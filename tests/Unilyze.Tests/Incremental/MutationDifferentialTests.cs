using Unilyze.Tests.Helpers;
using Xunit.Abstractions;

namespace Unilyze.Tests.Incremental;

// Mutation-differential harness skeleton (design doc §7.2,
// tasks/reverse-dependency-index-design.md in the main repo). Static exhaustiveness audits
// cannot see "types that influenced a resolution without being the resolution result" — the only
// way to catch that class of bug is to apply a semantics-shifting mutation and diff a full run
// against a warm-incremental run. Unlike SemanticIncrementalEquivalenceTests (one mutation atop a
// freshly seeded cache, asserted in isolation per Theory case), this harness applies a SEQUENCE of
// mutations to the SAME project without ever re-seeding: the goal is to verify the on-disk cache
// stays correct as it evolves across a long edit session, not just after a single edit.
//
// Every mutation below is a structural change (new member / new operator / new conversion / new
// base / retargeted using). Through Phase 0 every step fell back to a full re-enrich under the v1
// detector (StructuralChangeDetector + SyntaxIncrementalCollector.HasStructuralChange) —
// full/incremental equivalence held trivially. Phase A2 (design doc §4.3) replaced that blanket
// fallback with per-type delta classification: retarget-alias now resolves through the precise
// Δusing(F) path (SEED ∪ RDeps(F's types)) instead of a full re-enrich; add-member/add-operator/
// add-extension/change-base/add-conversion are member-add or base-change deltas, which still fall
// back to full until Phase B lands Δmembers/Δbase precision. Either way, this harness is the
// regression gate that catches an under-invalidation bug the moment it would first appear in an
// editing session (§7.2 "any divergence here is a P0").
//
// To add a mutation: append a MutationStep to MutationSequence with a Name and an
// Apply(projectRoot) file-rewrite. Phase B's hazard list (interface default member add, member
// hide via `new`, collection-initializer Add capture, foreach/await/deconstruct pattern member
// add, ...) plugs in the same way — no harness changes needed, just new steps.
public sealed class MutationDifferentialTests : IDisposable
{
    readonly string _projectRoot;
    readonly ITestOutputHelper _output;

    public MutationDifferentialTests(ITestOutputHelper output)
    {
        _output = output;
        _projectRoot = Path.Combine(Path.GetTempPath(), $"unilyze-mut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
        WriteBaselineFixture();
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    sealed record MutationStep(string Name, Action<string> Apply);

    // Deterministic seed list (§7.2: "deterministic seed list in CI ... minutes not hours" — no
    // randomness here). Each step targets its own file so the sequence's order doesn't matter for
    // correctness, only for narrative/coverage: every step is independently a full-reenrich
    // trigger under v1, and the assertion after each step catches a regression the moment it
    // would first appear in an editing session.
    static readonly IReadOnlyList<MutationStep> MutationSequence =
    [
        new("add-member", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "Widget.cs"), """
            namespace Sample;

            public class Widget
            {
                public int Value { get; set; }

                public int Double() => Value * 2;
            }
            """)),

        new("add-operator", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "OperatorHost.cs"), """
            namespace Sample;

            public class OperatorHost
            {
                public int Value { get; set; }

                public static OperatorHost operator +(OperatorHost a, OperatorHost b) =>
                    new() { Value = a.Value + b.Value };
            }
            """)),

        new("add-extension", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "ExtensionHost.cs"), """
            namespace Sample;

            public static class Extensions
            {
                public static int Tripled(this Widget w) => w.Value * 3;
            }
            """)),

        new("change-base", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "BaseHost.cs"), """
            namespace Sample;

            public class BaseA { }

            public class BaseB { }

            public class Changeling : BaseB { }
            """)),

        // Reuses the Phase 0a alias-retarget fixture shape (design doc §2): AliasHost.cs aliases
        // A to a shallow base (Bar); retargeting to Qux (deeper chain) changes AliasX's real base
        // chain without touching its declaration signature (base type text is still "A").
        new("retarget-alias", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "AliasHost.cs"), """
            using A = Sample.Ns2.Qux;

            namespace Sample;

            public class AliasX : A { }
            """)),

        // Adding an implicit conversion can shift which overload an existing call site binds to
        // (Consumer.Resolve currently binds Describe(object)); the conversion operator is itself
        // a new member on ConversionHost, so it is caught the same way add-operator is.
        new("add-conversion", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "ConversionHost.cs"), """
            namespace Sample;

            public class ConversionHost
            {
                public int Value { get; set; }

                public static implicit operator int(ConversionHost c) => c.Value;
            }
            """)),
    ];

    [Fact]
    public void MutationSequence_StaysEquivalentAcrossAnEvolvingWarmCache()
    {
        Analyze(incremental: true); // establish the warm cache ONCE; never re-seeded below

        foreach (var mutation in MutationSequence)
        {
            _output.WriteLine($"[mutation] applying {mutation.Name}");
            mutation.Apply(_projectRoot);

            var incremental = Analyze(incremental: true);
            var full = Analyze(incremental: false); // plain runs never touch the incremental cache
            Assert.Equal(IncrementalCliHelper.Normalize(full), IncrementalCliHelper.Normalize(incremental));
        }
    }

    void WriteBaselineFixture()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Widget.cs"), """
            namespace Sample;

            public class Widget
            {
                public int Value { get; set; }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "OperatorHost.cs"), """
            namespace Sample;

            public class OperatorHost
            {
                public int Value { get; set; }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "ExtensionHost.cs"), """
            namespace Sample;

            public static class Extensions
            {
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "BaseHost.cs"), """
            namespace Sample;

            public class BaseA { }

            public class BaseB { }

            public class Changeling : BaseA { }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "AliasBases.cs"), """
            namespace Sample.Ns1
            {
                public class Bar { }
            }

            namespace Sample.Ns2
            {
                public class QuxBase { }

                public class Qux : QuxBase { }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "AliasHost.cs"), """
            using A = Sample.Ns1.Bar;

            namespace Sample;

            public class AliasX : A { }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "ConversionHost.cs"), """
            namespace Sample;

            public class ConversionHost
            {
                public int Value { get; set; }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Consumer.cs"), """
            namespace Sample;

            public static class Consumer
            {
                public static string Describe(object o) => "object";
                public static string Describe(int i) => "int";

                public static string Resolve(ConversionHost c) => Describe(c);
            }
            """);
    }

    string Analyze(bool incremental)
    {
        var args = new List<string> { "-p", _projectRoot, "--level", "core", "-f", "json" };
        if (incremental)
            args.Add("--incremental");
        var (exitCode, stdout, stderr) = IncrementalCliHelper.Run(args.ToArray());
        Assert.True(exitCode == 0, $"analyze exited {exitCode}. stderr:{Environment.NewLine}{stderr}");
        return stdout;
    }
}
