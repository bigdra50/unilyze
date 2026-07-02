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
// fallback with per-type delta classification: retarget-alias resolves through the precise
// Δusing(F) path (SEED ∪ RDeps(F's types)) instead of a full re-enrich. Phase B (design doc §4.3,
// gated on THIS harness) replaced the remaining member-add/base-change full fallback with
// RDeps(B ∪ InhDesc(B)) / InhDesc(B) ∪ RDeps(B ∪ InhDesc(B)) — add-member/add-operator/
// add-extension/change-base/add-conversion above now flow through that precise path too. The
// hazard steps below (add-base-member-captures-derived-receiver onward) are Phase B's REQUIRED
// gate (§6): each targets a specific binding-capture risk the closure exists for — a receiver
// statically typed as a DESCENDANT of the changed type, which the declaration dependency graph
// alone (no closure) would miss. Either way, this harness is the regression gate that catches an
// under-invalidation bug the moment it would first appear in an editing session (§7.2 "any
// divergence here is a P0").
//
// To add a mutation: append a MutationStep to MutationSequence with a Name and an
// Apply(projectRoot) file-rewrite, and any needed fixture files in WriteBaselineFixture.
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

        // ---- Phase B hazard list (design doc §6, REQUIRED gate for Δmembers/Δbase precision).
        // Each step below is a binding-capture risk that only the InhDesc(B) closure — not RDeps(B)
        // alone — can catch: a caller's receiver is statically typed as a DESCENDANT of the type
        // whose member set/base list changes, so the caller's recorded UsedTypes never mentions B
        // itself. ----

        // CapCaptureCaller holds a CapDerived-typed receiver and calls the extension method
        // Widen() (no instance Widen exists on CapBase/CapDerived yet). Adding an instance Widen()
        // to CapBase captures that call: member resolution prefers instance members over extension
        // methods, so CapCaptureCaller.Use rebinds from CapExtensions.Widen to CapBase.Widen. This
        // is Δmembers(CapBase); catching CapCaptureCaller (which only ever mentions CapDerived, not
        // CapBase) requires RDeps(CapDerived) via InhDesc(CapBase) ∋ CapDerived.
        new("add-base-member-captures-derived-receiver",
            projectRoot => File.WriteAllText(Path.Combine(projectRoot, "CapCaptureHost.cs"), """
            namespace Sample;

            public class CapBase
            {
                public int Widen() => 2;
            }

            public class CapDerived : CapBase { }

            public static class CapExtensions
            {
                public static int Widen(this CapDerived d) => 1;
            }
            """)),

        // DimCaller has both an IDim-typed caller and a DimImpl (implementing-type)-typed caller.
        // Adding a default interface method to IDim is Δmembers(IDim); DimImplCaller (which only
        // ever mentions DimImpl, never IDim by name) needs RDeps(DimImpl) via
        // InhDesc(IDim) ∋ DimImpl to be swept in.
        new("add-interface-default-member", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "DimHost.cs"), """
            namespace Sample;

            public interface IDim
            {
                int Existing();
                int Added() => 42;
            }

            public class DimImpl : IDim
            {
                public int Existing() => 1;
            }
            """)),

        // HideCaller has both a HideBase-typed caller and a HideDerived-typed caller, both calling
        // M(). Before this mutation both calls bind to HideBase.M (HideDerived has no member of its
        // own yet). Adding `new int M()` to HideDerived hides HideBase.M for HideDerived-typed
        // receivers only — HideCaller.UseDerived rebinds while HideCaller.UseBase does not. This is
        // Δmembers(HideDerived) directly (HideDerived IS the changed type), so no closure is needed
        // to catch HideCaller — a baseline check that the non-static-class Δmembers path alone
        // (from Phase B's core rule, not the extension carve-out) stays correct.
        new("hide-base-member", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "HideHost.cs"), """
            namespace Sample;

            public class HideBase
            {
                public int M() => 1;
            }

            public class HideDerived : HideBase
            {
                public new int M() => 2;
            }
            """)),

        // OverloadShiftConsumer.ResolveDerived passes an OverloadShiftDerived-typed argument to an
        // overloaded Describe(object)/Describe(int) pair; before this mutation it binds
        // Describe(object) (no conversion to int exists). Adding an implicit conversion operator to
        // OverloadShiftBase (NOT OverloadShiftDerived) is Δmembers(OverloadShiftBase); C# applies a
        // base class's user-defined conversion to derived-typed operands, so ResolveDerived rebinds
        // to Describe(int) — but OverloadShiftConsumer only ever mentions OverloadShiftDerived, so
        // catching it needs RDeps(OverloadShiftDerived) via InhDesc(OverloadShiftBase).
        new("add-implicit-conversion-shifts-overload",
            projectRoot => File.WriteAllText(Path.Combine(projectRoot, "OverloadShiftBase.cs"), """
            namespace Sample;

            public class OverloadShiftBase
            {
                public int Value { get; set; }

                public static implicit operator int(OverloadShiftBase b) => b.Value;
            }

            public class OverloadShiftDerived : OverloadShiftBase { }
            """)),

        // CollCaller builds a CollBox via a collection initializer `{ 1, 2, 3 }`; before this
        // mutation CollBox has only Add(object), so each int literal binds through a boxing
        // conversion. Adding an Add(int) overload is Δmembers(CollBox); CollCaller's initializer
        // rebinds to the non-boxing overload, changing its recorded boxing occurrences.
        new("add-collection-initializer-add", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "CollHost.cs"), """
            namespace Sample;

            using System.Collections;
            using System.Collections.Generic;

            public class CollBox : IEnumerable
            {
                readonly List<object> _items = new();
                public void Add(object x) => _items.Add(x);
                public void Add(int x) => _items.Add(x);
                public IEnumerator GetEnumerator() => _items.GetEnumerator();
            }
            """)),

        // FeCaller iterates a FeBag with `foreach`; before this mutation FeBag has no instance
        // GetEnumerator, so the loop binds through the extension GetEnumerator in FeExtensions.
        // Adding an instance GetEnumerator directly to FeBag is Δmembers(FeBag) and captures the
        // foreach binding away from the extension (instance members win over extension methods for
        // the enumerator pattern too).
        new("add-foreach-pattern-member", projectRoot => File.WriteAllText(Path.Combine(projectRoot, "FePatternHost.cs"), """
            namespace Sample;

            using System.Collections.Generic;

            public class FeBag
            {
                public List<int> Items = new() { 1, 2, 3 };
                public IEnumerator<int> GetEnumerator() => Items.GetEnumerator();
            }

            public static class FeExtensions
            {
                public static IEnumerator<int> GetEnumerator(this FeBag bag) => bag.Items.GetEnumerator();
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

        // ---- Phase B hazard fixtures (paired with the mutation steps above). ----
        File.WriteAllText(Path.Combine(_projectRoot, "CapCaptureHost.cs"), """
            namespace Sample;

            public class CapBase { }

            public class CapDerived : CapBase { }

            public static class CapExtensions
            {
                public static int Widen(this CapDerived d) => 1;
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "CapCaptureCaller.cs"), """
            namespace Sample;

            public class CapCaptureCaller
            {
                public int Use(CapDerived d) => d.Widen();
            }
            """);

        File.WriteAllText(Path.Combine(_projectRoot, "DimHost.cs"), """
            namespace Sample;

            public interface IDim
            {
                int Existing();
            }

            public class DimImpl : IDim
            {
                public int Existing() => 1;
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "DimCaller.cs"), """
            namespace Sample;

            public class DimCaller
            {
                public int UseViaInterface(IDim x) => x.Existing();
                public int UseViaImplementation(DimImpl x) => x.Existing();
            }
            """);

        File.WriteAllText(Path.Combine(_projectRoot, "HideHost.cs"), """
            namespace Sample;

            public class HideBase
            {
                public int M() => 1;
            }

            public class HideDerived : HideBase { }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "HideCaller.cs"), """
            namespace Sample;

            public class HideCaller
            {
                public int UseBase(HideBase b) => b.M();
                public int UseDerived(HideDerived d) => d.M();
            }
            """);

        File.WriteAllText(Path.Combine(_projectRoot, "OverloadShiftBase.cs"), """
            namespace Sample;

            public class OverloadShiftBase
            {
                public int Value { get; set; }
            }

            public class OverloadShiftDerived : OverloadShiftBase { }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "OverloadShiftConsumer.cs"), """
            namespace Sample;

            public static class OverloadShiftConsumer
            {
                public static string Describe(object o) => "object";
                public static string Describe(int i) => "int";

                public static string ResolveDerived(OverloadShiftDerived d) => Describe(d);
                public static string ResolveDirect() => Describe(42);
            }
            """);

        File.WriteAllText(Path.Combine(_projectRoot, "CollHost.cs"), """
            namespace Sample;

            using System.Collections;
            using System.Collections.Generic;

            public class CollBox : IEnumerable
            {
                readonly List<object> _items = new();
                public void Add(object x) => _items.Add(x);
                public IEnumerator GetEnumerator() => _items.GetEnumerator();
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "CollCaller.cs"), """
            namespace Sample;

            public class CollCaller
            {
                public CollBox Build() => new CollBox { 1, 2, 3 };
            }
            """);

        File.WriteAllText(Path.Combine(_projectRoot, "FePatternHost.cs"), """
            namespace Sample;

            using System.Collections.Generic;

            public class FeBag
            {
                public List<int> Items = new() { 1, 2, 3 };
            }

            public static class FeExtensions
            {
                public static IEnumerator<int> GetEnumerator(this FeBag bag) => bag.Items.GetEnumerator();
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "FeCaller.cs"), """
            namespace Sample;

            public class FeCaller
            {
                public int Sum(FeBag bag)
                {
                    var total = 0;
                    foreach (var x in bag) total += x;
                    return total;
                }
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
