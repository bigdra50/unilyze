using Unilyze.Incremental;
using Unilyze.Pipeline;

namespace Unilyze.Tests.Incremental;

// InhDesc(B) (design doc §4.3-4.4 Phase B): the transitive inheritance/interface-implementation
// descendant closure used to resolve Δmembers(B)/Δbase(B) invalidation.
public sealed class InheritanceDescendantIndexTests
{
    [Fact]
    public void DirectChild_IsIncluded()
    {
        var deps = new[] { Dep("D", "B", DependencyKind.Inheritance) };
        var index = InheritanceDescendantIndex.Build(deps);

        Assert.Equal(["D"], InheritanceDescendantIndex.DescendantsOf(index, "B"));
    }

    // Multi-level inheritance: D2 : D1 : B — D2 must appear in InhDesc(B) even though only D1
    // has a direct Inheritance edge to B (a receiver statically typed D2 can still re-bind to a
    // member B gains/loses).
    [Fact]
    public void TransitiveGrandchild_IsIncluded()
    {
        var deps = new[]
        {
            Dep("D1", "B", DependencyKind.Inheritance),
            Dep("D2", "D1", DependencyKind.Inheritance),
        };
        var index = InheritanceDescendantIndex.Build(deps);

        Assert.Equal(
            new HashSet<string> { "D1", "D2" },
            InheritanceDescendantIndex.DescendantsOf(index, "B"));
    }

    [Fact]
    public void InterfaceImplementation_IsIncluded()
    {
        var deps = new[] { Dep("Impl", "IFoo", DependencyKind.InterfaceImpl) };
        var index = InheritanceDescendantIndex.Build(deps);

        Assert.Equal(["Impl"], InheritanceDescendantIndex.DescendantsOf(index, "IFoo"));
    }

    // Diamond: D implements both IFoo and IBar, both of which extend IBase (recorded as separate
    // InterfaceImpl/Inheritance edges into IBase from each). D must appear exactly once in
    // InhDesc(IBase) despite the two paths converging on it (HashSet-backed closure).
    [Fact]
    public void Diamond_DoesNotDuplicateDescendant()
    {
        var deps = new[]
        {
            Dep("IFoo", "IBase", DependencyKind.InterfaceImpl),
            Dep("IBar", "IBase", DependencyKind.InterfaceImpl),
            Dep("D", "IFoo", DependencyKind.InterfaceImpl),
            Dep("D", "IBar", DependencyKind.InterfaceImpl),
        };
        var index = InheritanceDescendantIndex.Build(deps);

        var descendants = InheritanceDescendantIndex.DescendantsOf(index, "IBase");
        Assert.Equal(new HashSet<string> { "IFoo", "IBar", "D" }, descendants);
    }

    // Non-inheritance dependency kinds (field/property/parameter/return types etc.) must not be
    // treated as inheritance edges — only Inheritance/InterfaceImpl contribute to the closure.
    [Fact]
    public void NonInheritanceDependencyKinds_AreIgnored()
    {
        var deps = new[]
        {
            Dep("Holder", "B", DependencyKind.FieldType),
            Dep("Caller", "B", DependencyKind.MethodParam),
        };
        var index = InheritanceDescendantIndex.Build(deps);

        Assert.Empty(InheritanceDescendantIndex.DescendantsOf(index, "B"));
    }

    [Fact]
    public void TypeWithNoDescendants_ReturnsEmptySet()
    {
        var deps = new[] { Dep("D", "B", DependencyKind.Inheritance) };
        var index = InheritanceDescendantIndex.Build(deps);

        Assert.Empty(InheritanceDescendantIndex.DescendantsOf(index, "D")); // leaf, not B
        Assert.Empty(InheritanceDescendantIndex.DescendantsOf(index, "Unrelated"));
    }

    static TypeDependency Dep(string fromTypeId, string toTypeId, DependencyKind kind) =>
        new(fromTypeId, toTypeId, kind, fromTypeId, toTypeId);
}
