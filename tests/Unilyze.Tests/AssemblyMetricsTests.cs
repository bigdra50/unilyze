namespace Unilyze.Tests;

public class AssemblyMetricsTests
{
    static TypeNodeInfo MakeType(string name, string kind, string ns = "",
        IReadOnlyList<string>? modifiers = null, IReadOnlyList<MemberInfo>? members = null)
        => new(name, ns, kind, modifiers ?? [], null, [], members ?? [],
            [], [], [], null, "TestAsm", "test.cs", false);

    [Fact]
    public void EmptyTypes_AllCountsZero()
    {
        var result = AssemblyMetrics.Compute("TestAsm", []);

        Assert.Equal("TestAsm", result.AssemblyName);
        Assert.Equal(0, result.TypeCount);
        Assert.Equal(0, result.ClassCount);
        Assert.Equal(0, result.RecordCount);
        Assert.Equal(0, result.InterfaceCount);
        Assert.Equal(0, result.EnumCount);
        Assert.Equal(0, result.DelegateCount);
        Assert.Equal(0, result.PublicTypeCount);
        Assert.Equal(0, result.SealedTypeCount);
        Assert.Equal(0, result.TotalMembers);
        Assert.Empty(result.Namespaces);
    }

    [Fact]
    public void SingleClass_TypeAndClassCountOne()
    {
        var types = new[] { MakeType("Foo", "class") };
        var result = AssemblyMetrics.Compute("TestAsm", types);

        Assert.Equal(1, result.TypeCount);
        Assert.Equal(1, result.ClassCount);
        Assert.Equal(0, result.RecordCount);
        Assert.Equal(0, result.InterfaceCount);
        Assert.Equal(0, result.EnumCount);
        Assert.Equal(0, result.DelegateCount);
    }

    [Fact]
    public void MixedTypes_EachCountCorrect()
    {
        var types = new[]
        {
            MakeType("C1", "class"),
            MakeType("I1", "interface"),
            MakeType("E1", "enum"),
            MakeType("D1", "delegate"),
            MakeType("R1", "record"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);

        Assert.Equal(5, result.TypeCount);
        Assert.Equal(1, result.ClassCount);
        Assert.Equal(1, result.InterfaceCount);
        Assert.Equal(1, result.EnumCount);
        Assert.Equal(1, result.DelegateCount);
        Assert.Equal(1, result.RecordCount);
    }

    [Fact]
    public void PublicTypes_CountedCorrectly()
    {
        var types = new[]
        {
            MakeType("Pub1", "class", modifiers: ["public"]),
            MakeType("Pub2", "class", modifiers: ["public", "sealed"]),
            MakeType("Internal", "class", modifiers: ["internal"]),
            MakeType("NoMod", "class"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);

        Assert.Equal(2, result.PublicTypeCount);
    }

    [Fact]
    public void SealedTypes_CountedCorrectly()
    {
        var types = new[]
        {
            MakeType("S1", "class", modifiers: ["sealed"]),
            MakeType("S2", "class", modifiers: ["public", "sealed"]),
            MakeType("Open", "class", modifiers: ["public"]),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);

        Assert.Equal(2, result.SealedTypeCount);
    }

    [Fact]
    public void TotalMembers_SumsAllTypeMemberCounts()
    {
        MemberInfo MakeMember(string name) => new(name, "void", "Method", [], [], []);

        var types = new[]
        {
            MakeType("A", "class", members: [MakeMember("M1"), MakeMember("M2")]),
            MakeType("B", "class", members: [MakeMember("M3")]),
            MakeType("C", "class"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);

        Assert.Equal(3, result.TotalMembers);
    }

    [Fact]
    public void Namespaces_DistinctSorted_EmptyExcluded()
    {
        var types = new[]
        {
            MakeType("A", "class", ns: "Z.Core"),
            MakeType("B", "class", ns: "A.Utils"),
            MakeType("C", "class", ns: "Z.Core"),   // duplicate
            MakeType("D", "class", ns: ""),          // empty - excluded
            MakeType("E", "class", ns: "M.Middle"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);

        Assert.Equal(new[] { "A.Utils", "M.Middle", "Z.Core" }, result.Namespaces);
    }

    [Fact]
    public void RecordStruct_CountedAsRecord()
    {
        var types = new[]
        {
            MakeType("RS1", "record struct"),
            MakeType("R1", "record"),
            MakeType("C1", "class"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);

        Assert.Equal(2, result.RecordCount);
        Assert.Equal(1, result.ClassCount);
    }

    // --- T4: Abstractness ---

    [Fact]
    public void Abstractness_EmptyTypes_ReturnsZero()
    {
        var result = AssemblyMetrics.Compute("TestAsm", []);
        Assert.Equal(0.0, result.Abstractness);
    }

    [Fact]
    public void Abstractness_NoAbstractTypes_ReturnsZero()
    {
        var types = new[]
        {
            MakeType("C1", "class"),
            MakeType("C2", "class", modifiers: ["public"]),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);
        Assert.Equal(0.0, result.Abstractness);
    }

    [Fact]
    public void Abstractness_AllAbstract_ReturnsOne()
    {
        var types = new[]
        {
            MakeType("A1", "class", modifiers: ["abstract"]),
            MakeType("I1", "interface"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);
        Assert.Equal(1.0, result.Abstractness);
    }

    [Fact]
    public void Abstractness_MixedTypes_CorrectRatio()
    {
        var types = new[]
        {
            MakeType("A1", "class", modifiers: ["abstract"]),
            MakeType("I1", "interface"),
            MakeType("C1", "class"),
            MakeType("E1", "enum"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types);
        Assert.Equal(0.5, result.Abstractness);
    }

    // --- T5: Distance from Main Sequence ---

    [Fact]
    public void DfMS_NoCouplingMap_ReturnsNull()
    {
        var types = new[] { MakeType("C1", "class") };
        var result = AssemblyMetrics.Compute("TestAsm", types);
        Assert.Null(result.DistanceFromMainSequence);
    }

    [Fact]
    public void DfMS_A0_I1_ReturnsZero()
    {
        // A=0 (no abstract), I=1 (all Ce, no Ca) => D=|0+1-1|=0
        var types = new[] { MakeType("C1", "class") };
        var typeId = TypeIdentity.GetTypeId(types[0]);
        var couplingMap = new Dictionary<string, CouplingInfo>
        {
            [typeId] = new CouplingInfo(AfferentCoupling: 0, EfferentCoupling: 5, Instability: 1.0)
        };
        var result = AssemblyMetrics.Compute("TestAsm", types, couplingMap: couplingMap);
        Assert.Equal(0.0, result.DistanceFromMainSequence);
    }

    [Fact]
    public void DfMS_A1_I0_ReturnsZero()
    {
        // A=1 (all abstract), I=0 (all Ca, no Ce) => D=|1+0-1|=0
        var types = new[] { MakeType("I1", "interface") };
        var typeId = TypeIdentity.GetTypeId(types[0]);
        var couplingMap = new Dictionary<string, CouplingInfo>
        {
            [typeId] = new CouplingInfo(AfferentCoupling: 5, EfferentCoupling: 0, Instability: 0.0)
        };
        var result = AssemblyMetrics.Compute("TestAsm", types, couplingMap: couplingMap);
        Assert.Equal(0.0, result.DistanceFromMainSequence);
    }

    [Fact]
    public void DfMS_A05_I05_ReturnsZero()
    {
        // A=0.5 (1 abstract / 2 total), I=0.5 => D=|0.5+0.5-1|=0
        var types = new[]
        {
            MakeType("I1", "interface"),
            MakeType("C1", "class"),
        };
        var couplingMap = new Dictionary<string, CouplingInfo>
        {
            [TypeIdentity.GetTypeId(types[0])] = new(AfferentCoupling: 3, EfferentCoupling: 3, Instability: 0.5),
            [TypeIdentity.GetTypeId(types[1])] = new(AfferentCoupling: 2, EfferentCoupling: 2, Instability: 0.5),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types, couplingMap: couplingMap);
        Assert.Equal(0.0, result.DistanceFromMainSequence);
    }

    [Fact]
    public void DfMS_A0_I0_ReturnsOne()
    {
        // A=0 (no abstract), I=0 (no coupling at all => 0) => D=|0+0-1|=1
        var types = new[] { MakeType("C1", "class") };
        var typeId = TypeIdentity.GetTypeId(types[0]);
        var couplingMap = new Dictionary<string, CouplingInfo>
        {
            [typeId] = new CouplingInfo(AfferentCoupling: 0, EfferentCoupling: 0, Instability: null)
        };
        var result = AssemblyMetrics.Compute("TestAsm", types, couplingMap: couplingMap);
        Assert.Equal(1.0, result.DistanceFromMainSequence);
    }

    // --- T6: RelationalCohesion ---

    [Fact]
    public void RelationalCohesion_NoDependencies_ReturnsNull()
    {
        var types = new[] { MakeType("C1", "class"), MakeType("C2", "class") };
        var result = AssemblyMetrics.Compute("TestAsm", types);
        Assert.Null(result.RelationalCohesion);
    }

    [Fact]
    public void RelationalCohesion_SingleType_ReturnsNull()
    {
        var types = new[] { MakeType("C1", "class") };
        var deps = Array.Empty<TypeDependency>();
        var result = AssemblyMetrics.Compute("TestAsm", types, dependencies: deps);
        Assert.Null(result.RelationalCohesion);
    }

    [Fact]
    public void RelationalCohesion_NoInternalDeps_ReturnsMinimum()
    {
        // N=2, R=0 => H = (0+1)/2 = 0.5
        var types = new[]
        {
            MakeType("C1", "class"),
            MakeType("C2", "class"),
        };
        var deps = Array.Empty<TypeDependency>();
        var result = AssemblyMetrics.Compute("TestAsm", types, dependencies: deps);
        Assert.Equal(0.5, result.RelationalCohesion);
    }

    [Fact]
    public void RelationalCohesion_WithInternalDeps_CorrectCalculation()
    {
        // N=3, R=2 (C1->C2, C2->C3) => H = (2+1)/3 = 1.0
        var types = new[]
        {
            MakeType("C1", "class"),
            MakeType("C2", "class"),
            MakeType("C3", "class"),
        };
        var id1 = TypeIdentity.GetTypeId(types[0]);
        var id2 = TypeIdentity.GetTypeId(types[1]);
        var id3 = TypeIdentity.GetTypeId(types[2]);
        var deps = new TypeDependency[]
        {
            new("C1", "C2", DependencyKind.FieldType, id1, id2),
            new("C2", "C3", DependencyKind.FieldType, id2, id3),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types, dependencies: deps);
        Assert.Equal(1.0, result.RelationalCohesion);
    }

    [Fact]
    public void RelationalCohesion_DuplicateDeps_CountedOnce()
    {
        // N=2, R=1 (same dep twice) => H = (1+1)/2 = 1.0
        var types = new[]
        {
            MakeType("C1", "class"),
            MakeType("C2", "class"),
        };
        var id1 = TypeIdentity.GetTypeId(types[0]);
        var id2 = TypeIdentity.GetTypeId(types[1]);
        var deps = new TypeDependency[]
        {
            new("C1", "C2", DependencyKind.FieldType, id1, id2),
            new("C1", "C2", DependencyKind.PropertyType, id1, id2),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types, dependencies: deps);
        Assert.Equal(1.0, result.RelationalCohesion);
    }

    [Fact]
    public void RelationalCohesion_CrossAssemblyDeps_NotCounted()
    {
        // N=2, cross-assembly deps should be ignored => R=0 => H = (0+1)/2 = 0.5
        var types = new[]
        {
            MakeType("C1", "class"),
            MakeType("C2", "class"),
        };
        var id1 = TypeIdentity.GetTypeId(types[0]);
        var deps = new TypeDependency[]
        {
            new("C1", "X1", DependencyKind.FieldType, id1, "OtherAsm::X1"),
        };
        var result = AssemblyMetrics.Compute("TestAsm", types, dependencies: deps);
        Assert.Equal(0.5, result.RelationalCohesion);
    }
}
