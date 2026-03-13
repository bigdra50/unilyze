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
}
