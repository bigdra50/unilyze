using Unilyze.Incremental;
using Unilyze.Pipeline;

namespace Unilyze.Tests.Incremental;

public sealed class StructuralChangeDetectorTests
{
    [Fact]
    public void BodyOnlyChange_IsNotStructural()
    {
        var before = new[] { Type("Delta", members: [Method("Seed", "int", cyclomatic: 1)]) };
        var after = new[] { Type("Delta", members: [Method("Seed", "int", cyclomatic: 9)]) };

        // Same signature, different body-derived complexity -> not a structural change.
        Assert.False(StructuralChangeDetector.FileStructureChanged(before, after));
    }

    [Fact]
    public void AddedMember_IsStructural()
    {
        var before = new[] { Type("Delta", members: [Method("Seed", "int")]) };
        var after = new[] { Type("Delta", members: [Method("Seed", "int"), Method("Extra", "int")]) };

        Assert.True(StructuralChangeDetector.FileStructureChanged(before, after));
    }

    [Fact]
    public void ChangedReturnType_IsStructural()
    {
        var before = new[] { Type("Delta", members: [Method("Seed", "int")]) };
        var after = new[] { Type("Delta", members: [Method("Seed", "long")]) };

        Assert.True(StructuralChangeDetector.FileStructureChanged(before, after));
    }

    [Fact]
    public void ChangedBaseType_IsStructural()
    {
        var before = new[] { Type("Delta", baseType: null) };
        var after = new[] { Type("Delta", baseType: "Origin") };

        Assert.True(StructuralChangeDetector.FileStructureChanged(before, after));
    }

    [Fact]
    public void ReorderedMembers_AreNotStructural()
    {
        var before = new[] { Type("Delta", members: [Method("A", "int"), Method("B", "int")]) };
        var after = new[] { Type("Delta", members: [Method("B", "int"), Method("A", "int")]) };

        Assert.False(StructuralChangeDetector.FileStructureChanged(before, after));
    }

    [Fact]
    public void AddedTypeInFile_IsStructural()
    {
        var before = new[] { Type("Delta") };
        var after = new[] { Type("Delta"), Type("Sidecar") };

        Assert.True(StructuralChangeDetector.FileStructureChanged(before, after));
    }

    static TypeNodeInfo Type(
        string name,
        string? baseType = null,
        IReadOnlyList<MemberInfo>? members = null) =>
        new(
            Name: name,
            Namespace: "Sample",
            Kind: "class",
            Modifiers: ["public"],
            BaseType: baseType,
            Interfaces: [],
            Members: members ?? [],
            ConstructorParams: [],
            Attributes: [],
            GenericConstraints: [],
            EnumBaseType: null,
            Assembly: "Assembly-CSharp",
            FilePath: $"/src/{name}.cs",
            IsNested: false);

    static MemberInfo Method(string name, string returnType, int cyclomatic = 1) =>
        new(
            Name: name,
            Type: returnType,
            MemberKind: "Method",
            Modifiers: ["public"],
            Parameters: [],
            Attributes: [],
            CyclomaticComplexity: cyclomatic,
            LineCount: cyclomatic + 1);
}
