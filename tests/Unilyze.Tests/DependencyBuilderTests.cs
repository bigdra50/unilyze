namespace Unilyze.Tests;

public class DependencyBuilderTests
{
    static TypeNodeInfo MakeType(string name, string? baseType = null,
        IReadOnlyList<string>? interfaces = null, IReadOnlyList<MemberInfo>? members = null,
        IReadOnlyList<string>? ctorParams = null, IReadOnlyList<GenericConstraintInfo>? constraints = null)
        => new(name, "", "class", [], baseType, interfaces ?? [], members ?? [],
            ctorParams ?? [], [], constraints ?? [], null, "TestAsm", "test.cs", false);

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var result = DependencyBuilder.Build([]);

        Assert.Empty(result);
    }

    [Fact]
    public void SingleTypeNoDependencies_ReturnsEmpty()
    {
        var types = new[] { MakeType("Foo") };

        var result = DependencyBuilder.Build(types);

        Assert.Empty(result);
    }

    [Fact]
    public void Inheritance_DetectsBaseType()
    {
        var types = new[]
        {
            MakeType("Base"),
            MakeType("Derived", baseType: "Base"),
        };

        var result = DependencyBuilder.Build(types);

        var dep = Assert.Single(result);
        Assert.Equal("Derived", dep.FromType);
        Assert.Equal("Base", dep.ToType);
        Assert.Equal(DependencyKind.Inheritance, dep.Kind);
    }

    [Fact]
    public void InterfaceImpl_DetectsInterfaces()
    {
        var types = new[]
        {
            MakeType("IFoo"),
            MakeType("Bar", interfaces: ["IFoo"]),
        };

        var result = DependencyBuilder.Build(types);

        var dep = Assert.Single(result);
        Assert.Equal("Bar", dep.FromType);
        Assert.Equal("IFoo", dep.ToType);
        Assert.Equal(DependencyKind.InterfaceImpl, dep.Kind);
    }

    [Fact]
    public void FieldType_DetectsMemberField()
    {
        var field = new MemberInfo("_svc", "Service", "Field", [], [], []);
        var types = new[]
        {
            MakeType("Service"),
            MakeType("Consumer", members: [field]),
        };

        var result = DependencyBuilder.Build(types);

        var dep = Assert.Single(result);
        Assert.Equal("Consumer", dep.FromType);
        Assert.Equal("Service", dep.ToType);
        Assert.Equal(DependencyKind.FieldType, dep.Kind);
    }

    [Fact]
    public void PropertyType_DetectsMemberProperty()
    {
        var prop = new MemberInfo("Svc", "Service", "Property", [], [], []);
        var types = new[]
        {
            MakeType("Service"),
            MakeType("Consumer", members: [prop]),
        };

        var result = DependencyBuilder.Build(types);

        var dep = Assert.Single(result);
        Assert.Equal("Consumer", dep.FromType);
        Assert.Equal("Service", dep.ToType);
        Assert.Equal(DependencyKind.PropertyType, dep.Kind);
    }

    [Fact]
    public void ConstructorParam_DetectsCtorDependency()
    {
        var types = new[]
        {
            MakeType("Dep"),
            MakeType("Host", ctorParams: ["Dep"]),
        };

        var result = DependencyBuilder.Build(types);

        var dep = Assert.Single(result);
        Assert.Equal("Host", dep.FromType);
        Assert.Equal("Dep", dep.ToType);
        Assert.Equal(DependencyKind.ConstructorParam, dep.Kind);
    }

    [Fact]
    public void MethodParam_DetectsParameterDependency()
    {
        var method = new MemberInfo("DoWork", "void", "Method", [],
            [new ParameterInfo("arg", "Helper")], []);
        var types = new[]
        {
            MakeType("Helper"),
            MakeType("Worker", members: [method]),
        };

        var result = DependencyBuilder.Build(types);

        Assert.Contains(result,
            d => d.FromType == "Worker" && d.ToType == "Helper" && d.Kind == DependencyKind.MethodParam);
    }

    [Fact]
    public void ReturnType_DetectsMethodReturn()
    {
        var method = new MemberInfo("Create", "Product", "Method", [], [], []);
        var types = new[]
        {
            MakeType("Product"),
            MakeType("Factory", members: [method]),
        };

        var result = DependencyBuilder.Build(types);

        Assert.Contains(result,
            d => d.FromType == "Factory" && d.ToType == "Product" && d.Kind == DependencyKind.ReturnType);
    }

    [Fact]
    public void GenericConstraint_DetectsConstraintDependency()
    {
        var constraint = new GenericConstraintInfo("T", ["Base"]);
        var types = new[]
        {
            MakeType("Base"),
            MakeType("Generic", constraints: [constraint]),
        };

        var result = DependencyBuilder.Build(types);

        var dep = Assert.Single(result);
        Assert.Equal("Generic", dep.FromType);
        Assert.Equal("Base", dep.ToType);
        Assert.Equal(DependencyKind.GenericConstraint, dep.Kind);
    }

    [Fact]
    public void SelfReference_Excluded()
    {
        var field = new MemberInfo("_self", "Node", "Field", [], [], []);
        var types = new[] { MakeType("Node", members: [field]) };

        var result = DependencyBuilder.Build(types);

        Assert.Empty(result);
    }

    [Fact]
    public void UnknownType_Ignored()
    {
        var field = new MemberInfo("_ext", "ExternalLib", "Field", [], [], []);
        var types = new[] { MakeType("MyClass", members: [field]) };

        var result = DependencyBuilder.Build(types);

        Assert.Empty(result);
    }

    [Fact]
    public void GenericTypeName_ExtractsInnerTypeArgs()
    {
        // List<Foo> should extract "List" and "Foo". Only "Foo" is a known type.
        var field = new MemberInfo("_items", "List<Foo>", "Field", [], [], []);
        var types = new[]
        {
            MakeType("Foo"),
            MakeType("Container", members: [field]),
        };

        var result = DependencyBuilder.Build(types);

        // "List" is unknown, so only Foo should produce an edge
        var dep = Assert.Single(result);
        Assert.Equal("Container", dep.FromType);
        Assert.Equal("Foo", dep.ToType);
        Assert.Equal(DependencyKind.FieldType, dep.Kind);
    }
}
