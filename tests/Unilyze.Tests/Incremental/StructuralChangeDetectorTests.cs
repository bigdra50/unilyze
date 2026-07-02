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

    // ---- ClassifyFileTypeDelta (design doc §4.3): per-TypeId delta classification that
    // refines FileStructureChanged's binary verdict into Δsig (precise, RDeps-resolved) vs
    // full-fallback (member add/remove, base/interface change, type add/delete). ----

    [Fact]
    public void SignatureModify_MemberSetAndBaseUnchanged_IsDeltaSig()
    {
        var before = new[] { Type("Delta", members: [Method("Seed", "int")]) };
        var after = new[] { Type("Delta", members: [Method("Seed", "long")]) }; // return type only

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.False(result.RequiresFull);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.SigChangedTypeIds);
    }

    [Fact]
    public void BodyOnlyChange_ClassifiesAsNoSigChange()
    {
        var before = new[] { Type("Delta", members: [Method("Seed", "int", cyclomatic: 1)]) };
        var after = new[] { Type("Delta", members: [Method("Seed", "int", cyclomatic: 9)]) };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.False(result.RequiresFull);
        Assert.Empty(result.SigChangedTypeIds);
    }

    // Phase B (design doc §4.3): a member add/remove on a non-static type is no longer a full
    // fallback — it classifies as Δmembers(B), resolved by the caller through
    // RDeps(B ∪ InhDesc(B)) instead of re-enriching everything.
    [Fact]
    public void MemberAdded_IsDeltaMembers()
    {
        var before = new[] { Type("Delta", members: [Method("Seed", "int")]) };
        var after = new[] { Type("Delta", members: [Method("Seed", "int"), Method("Extra", "int")]) };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.False(result.RequiresFull);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.MembersChangedTypeIds);
        Assert.Empty(result.SigChangedTypeIds);
        Assert.Empty(result.BaseChangedTypeIds);
    }

    // An overload add/remove keeps the SAME name+kind for the pre-existing member but changes
    // the (Name|MemberKind) MULTISET (two "Seed|Method" entries vs one), so it must count as a
    // member-set change even though no member simply vanished (design doc §4.3).
    [Fact]
    public void OverloadAdded_IsMemberSetChange_IsDeltaMembers()
    {
        var overload = new MemberInfo(
            Name: "Seed", Type: "int", MemberKind: "Method",
            Modifiers: ["public"], Parameters: [new ParameterInfo("x", "string")], Attributes: []);
        var before = new[] { Type("Delta", members: [Method("Seed", "int")]) };
        var after = new[] { Type("Delta", members: [Method("Seed", "int"), overload]) };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.False(result.RequiresFull);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.MembersChangedTypeIds);
    }

    // A member add/remove inside a STATIC class is conservatively kept as a full fallback: an
    // extension method is a `this`-first-parameter method declared in a static class, and raw
    // ParameterInfo carries no `this`-modifier flag, so the precise Δmembers rule for extension
    // methods (RDeps(P ∪ InhDesc(P)) for the receiver type P) can't be distinguished syntactically
    // here (design doc §4.3 deviation, documented in ClassifyFileTypeDelta).
    [Fact]
    public void StaticClassMemberAdded_RequiresFull()
    {
        var before = new[] { Type("Extensions", modifiers: ["public", "static"], members: []) };
        var after = new[]
        {
            Type("Extensions", modifiers: ["public", "static"], members: [Method("Widen", "int")]),
        };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.True(result.RequiresFull);
        Assert.Equal(
            "a static class's member set changed (possible extension method)", result.FullReason);
    }

    // ConstructorParams changes (primary constructors) are downgraded from full to Δmembers per
    // design doc §4.3 Phase B ("ConstructorParams change ... downgraded to Δmembers").
    [Fact]
    public void ConstructorParamsChanged_IsDeltaMembers()
    {
        var before = Type("Delta") with { ConstructorParams = ["int"] };
        var after = Type("Delta") with { ConstructorParams = ["int", "string"] };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta([before], [after]);

        Assert.False(result.RequiresFull);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.MembersChangedTypeIds);
    }

    // Phase B (design doc §4.3): a base-type change on a non-static type is no longer a full
    // fallback — it classifies as Δbase(B), resolved by the caller through
    // InhDesc(B) ∪ RDeps(B ∪ InhDesc(B)).
    [Fact]
    public void BaseTypeChanged_IsDeltaBase()
    {
        var before = new[] { Type("Delta", baseType: "Origin") };
        var after = new[] { Type("Delta", baseType: "OtherOrigin") };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.False(result.RequiresFull);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.BaseChangedTypeIds);
        Assert.Empty(result.MembersChangedTypeIds);
        Assert.Empty(result.SigChangedTypeIds);
    }

    [Fact]
    public void InterfaceChanged_IsDeltaBase()
    {
        var before = Type("Delta") with { Interfaces = ["IFoo"] };
        var after = Type("Delta") with { Interfaces = ["IFoo", "IBar"] };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta([before], [after]);

        Assert.False(result.RequiresFull);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.BaseChangedTypeIds);
    }

    // A single edit can touch both the member set and the base list at once — both delta lists
    // must carry the TypeId so the caller unions both invalidation rules (harmless: Δbase's set
    // is already a superset of Δmembers's for the same B).
    [Fact]
    public void MemberAddedAndBaseChanged_IsBothDeltaMembersAndDeltaBase()
    {
        var before = Type("Delta", baseType: "Origin", members: [Method("Seed", "int")]);
        var after = Type(
            "Delta", baseType: "OtherOrigin", members: [Method("Seed", "int"), Method("Extra", "int")]);

        var result = StructuralChangeDetector.ClassifyFileTypeDelta([before], [after]);

        Assert.False(result.RequiresFull);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.MembersChangedTypeIds);
        Assert.Equal(["Assembly-CSharp::Sample.Delta"], result.BaseChangedTypeIds);
    }

    [Fact]
    public void TypeAddedInFile_RequiresFull()
    {
        var before = new[] { Type("Delta") };
        var after = new[] { Type("Delta"), Type("Sidecar") };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.True(result.RequiresFull);
        Assert.Equal("a type was added", result.FullReason);
    }

    [Fact]
    public void TypeRemovedFromFile_RequiresFull()
    {
        var before = new[] { Type("Delta"), Type("Sidecar") };
        var after = new[] { Type("Delta") };

        var result = StructuralChangeDetector.ClassifyFileTypeDelta(before, after);

        Assert.True(result.RequiresFull);
        Assert.Equal("a type was removed", result.FullReason);
    }

    static TypeNodeInfo Type(
        string name,
        string? baseType = null,
        IReadOnlyList<MemberInfo>? members = null,
        IReadOnlyList<string>? modifiers = null) =>
        new(
            Name: name,
            Namespace: "Sample",
            Kind: "class",
            Modifiers: modifiers ?? ["public"],
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
