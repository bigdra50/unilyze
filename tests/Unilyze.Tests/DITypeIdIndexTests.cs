namespace Unilyze.Tests;

// Direct coverage for DITypeIdIndex resolution rules (issue 19):
// qualified-name keys win; simple-name keys resolve only when unique; otherwise null.
public sealed class DITypeIdIndexTests
{
    static TypeNodeInfo Type(string ns, string name)
        => new(
            name, ns, "class", [], null, [], [], [], [], [], null,
            "Asm", "test.cs", false);

    [Fact]
    public void Resolve_UniqueSimpleName_ResolvesToTypeId()
    {
        var foo = Type("Game.Services", "Foo");
        var index = DITypeIdIndex.Build([foo, Type("Game.Services", "Bar")]);

        Assert.Equal(TypeIdentity.GetTypeId(foo), index.Resolve("Foo", qualifiedName: null));
    }

    [Fact]
    public void Resolve_QualifiedName_TakesPrecedenceOverSimpleName()
    {
        // Two types share simple name "Foo"; a qualified key still resolves uniquely.
        var fooA = Type("Game.A", "Foo");
        var fooB = Type("Game.B", "Foo");
        var index = DITypeIdIndex.Build([fooA, fooB]);

        Assert.Equal(TypeIdentity.GetTypeId(fooB), index.Resolve("Foo", "Game.B.Foo"));
    }

    [Fact]
    public void Resolve_AmbiguousSimpleName_NoQualifier_ReturnsNull()
    {
        // Same simple name "Foo" in two namespaces, no qualifier to disambiguate.
        var index = DITypeIdIndex.Build([Type("Game.A", "Foo"), Type("Game.B", "Foo")]);

        Assert.Null(index.Resolve("Foo", qualifiedName: null));
    }

    [Fact]
    public void Resolve_AbsentType_ReturnsNull()
    {
        var index = DITypeIdIndex.Build([Type("Game.Services", "Foo")]);

        Assert.Null(index.Resolve("ExternalOnly", "ExternalOnly.IService"));
    }

    [Fact]
    public void Resolve_QualifiedNameMissesButSimpleNameUnique_FallsBackToSimpleName()
    {
        // A qualified candidate that is not in the set falls through to the unique
        // simple-name match (mirrors a fully-qualified write of an aliased namespace).
        var foo = Type("Game.Services", "Foo");
        var index = DITypeIdIndex.Build([foo]);

        Assert.Equal(TypeIdentity.GetTypeId(foo), index.Resolve("Foo", "Other.Ns.Foo"));
    }
}
