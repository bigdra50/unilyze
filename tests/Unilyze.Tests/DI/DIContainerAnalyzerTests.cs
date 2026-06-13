using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests.DI;

public class DIContainerAnalyzerTests
{
    static IReadOnlyList<DIRegistration> AnalyzeSyntactic(string code)
    {
        var tree = RoslynTestHelper.ParseCode(code);
        return DIContainerAnalyzer.Analyze([tree], compilation: null);
    }

    static IReadOnlyList<DIRegistration> AnalyzeSemantic(string code)
    {
        var tree = RoslynTestHelper.ParseCode(code);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return DIContainerAnalyzer.Analyze([tree], compilation);
    }

    // --- VContainer: Syntactic ---

    [Fact]
    public void VContainer_Register_TwoTypeArgs_Syntactic()
    {
        var code = """
            class IService { }
            class ServiceImpl { }
            enum Lifetime { Singleton, Transient, Scoped }
            class Installer {
                void Configure(object builder) {
                    builder.Register<IService, ServiceImpl>(Lifetime.Singleton);
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("IService", reg.ServiceType);
        Assert.Equal("ServiceImpl", reg.ImplementationType);
        Assert.Equal("VContainer", reg.ContainerType);
        Assert.Equal("Singleton", reg.Lifetime);
    }

    [Fact]
    public void VContainer_Register_OneTypeArg_Syntactic()
    {
        var code = """
            enum Lifetime { Singleton, Transient, Scoped }
            class MyService { }
            class Installer {
                void Configure(object builder) {
                    builder.Register<MyService>(Lifetime.Transient);
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("MyService", reg.ServiceType);
        Assert.Equal("MyService", reg.ImplementationType);
        Assert.Equal("VContainer", reg.ContainerType);
        Assert.Equal("Transient", reg.Lifetime);
    }

    [Fact]
    public void VContainer_RegisterInstance_Syntactic()
    {
        var code = """
            class Installer {
                void Configure(object builder) {
                    builder.RegisterInstance(myInstance);
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("myInstance", reg.ServiceType);
        Assert.Equal("myInstance", reg.ImplementationType);
        Assert.Equal("VContainer", reg.ContainerType);
        Assert.Equal("Singleton", reg.Lifetime);
    }

    [Fact]
    public void VContainer_RegisterFactory_Syntactic()
    {
        var code = """
            class Enemy { }
            class Installer {
                void Configure(object builder) {
                    builder.RegisterFactory<Enemy>(() => new Enemy());
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("Enemy", reg.ServiceType);
        Assert.Equal("Enemy", reg.ImplementationType);
        Assert.Equal("VContainer", reg.ContainerType);
        Assert.Equal("Transient", reg.Lifetime);
    }

    [Fact]
    public void VContainer_InjectAttribute_Syntactic()
    {
        var code = """
            class Inject : System.Attribute { }
            class PlayerController {
                [Inject]
                IMovementService _movement;
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("IMovementService", reg.ServiceType);
        Assert.Equal("IMovementService", reg.ImplementationType);
        Assert.Contains(reg.ContainerType, new[] { "VContainer", "Zenject", "Unknown" });
    }

    // --- Zenject: Syntactic ---

    [Fact]
    public void Zenject_Bind_To_AsSingle_Syntactic()
    {
        var code = """
            class IService { }
            class ServiceImpl { }
            class Installer {
                void Configure(object container) {
                    container.Bind<IService>().To<ServiceImpl>().AsSingle();
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("IService", reg.ServiceType);
        Assert.Equal("ServiceImpl", reg.ImplementationType);
        Assert.Equal("Zenject", reg.ContainerType);
        Assert.Equal("Singleton", reg.Lifetime);
    }

    [Fact]
    public void Zenject_Bind_To_AsTransient_Syntactic()
    {
        var code = """
            class IService { }
            class ServiceImpl { }
            class Installer {
                void Configure(object container) {
                    container.Bind<IService>().To<ServiceImpl>().AsTransient();
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("IService", reg.ServiceType);
        Assert.Equal("ServiceImpl", reg.ImplementationType);
        Assert.Equal("Zenject", reg.ContainerType);
        Assert.Equal("Transient", reg.Lifetime);
    }

    [Fact]
    public void Zenject_BindInterfacesTo_Syntactic()
    {
        var code = """
            class ServiceImpl { }
            class Installer {
                void Configure(object container) {
                    container.BindInterfacesTo<ServiceImpl>().AsSingle();
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("ServiceImpl", reg.ServiceType);
        Assert.Equal("ServiceImpl", reg.ImplementationType);
        Assert.Equal("Zenject", reg.ContainerType);
        Assert.Equal("Singleton", reg.Lifetime);
    }

    [Fact]
    public void Zenject_BindInterfacesAndSelfTo_Syntactic()
    {
        var code = """
            class ServiceImpl { }
            class Installer {
                void Configure(object container) {
                    container.BindInterfacesAndSelfTo<ServiceImpl>().AsCached();
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.Equal("ServiceImpl", reg.ServiceType);
        Assert.Equal("ServiceImpl", reg.ImplementationType);
        Assert.Equal("Zenject", reg.ContainerType);
        Assert.Equal("Scoped", reg.Lifetime);
    }

    // --- Common ---

    [Fact]
    public void NoDICode_ReturnsEmptyList()
    {
        var code = """
            class Calculator {
                int Add(int a, int b) => a + b;
                void DoWork() {
                    var x = new System.Collections.Generic.List<int>();
                    x.Add(1);
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        Assert.Empty(regs);
    }

    [Fact]
    public void NoDICode_Semantic_ReturnsEmptyList()
    {
        var code = """
            class Calculator {
                int Add(int a, int b) => a + b;
                void DoWork() {
                    var x = new System.Collections.Generic.List<int>();
                    x.Add(1);
                }
            }
            """;
        var regs = AnalyzeSemantic(code);
        Assert.Empty(regs);
    }

    [Fact]
    public void CompilationNull_SyntacticFallback_DetectsRegister()
    {
        var code = """
            enum Lifetime { Singleton }
            class IFoo { }
            class Foo { }
            class Setup {
                void Install(object builder) {
                    builder.Register<IFoo, Foo>(Lifetime.Singleton);
                }
            }
            """;
        // compilation == null triggers syntactic fallback
        var tree = RoslynTestHelper.ParseCode(code);
        var regs = DIContainerAnalyzer.Analyze([tree], compilation: null);
        var reg = Assert.Single(regs);
        Assert.Equal("IFoo", reg.ServiceType);
        Assert.Equal("Foo", reg.ImplementationType);
        Assert.Equal("VContainer", reg.ContainerType);
    }

    [Fact]
    public void MultipleRegistrations_AllDetected()
    {
        var code = """
            enum Lifetime { Singleton, Transient }
            class IA { }
            class A { }
            class IB { }
            class B { }
            class Installer {
                void Configure(object builder, object container) {
                    builder.Register<IA, A>(Lifetime.Singleton);
                    builder.Register<IB, B>(Lifetime.Transient);
                    container.Bind<IA>().To<A>().AsSingle();
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        Assert.Equal(3, regs.Count);
    }

    [Fact]
    public void FilePath_And_Line_ArePopulated()
    {
        var code = """
            enum Lifetime { Singleton }
            class IService { }
            class ServiceImpl { }
            class Installer {
                void Configure(object builder) {
                    builder.Register<IService, ServiceImpl>(Lifetime.Singleton);
                }
            }
            """;
        var regs = AnalyzeSyntactic(code);
        var reg = Assert.Single(regs);
        Assert.True(reg.Line > 0);
    }

    // --- Qualified name resolution (issue 19) ---

    [Fact]
    public void VContainer_Register_Semantic_PopulatesQualifiedNames()
    {
        // A VContainer stub lets the semantic model bind builder.Register<,>(), so the
        // qualified names come from the resolved type symbols even though the source
        // writes bare names via `using` (something the syntactic path cannot recover).
        var code = """
            namespace VContainer {
                public enum Lifetime { Singleton, Transient, Scoped }
                public interface IContainerBuilder { }
                public static class Registration {
                    public static void Register<TInterface, TImplementation>(
                        this IContainerBuilder builder, Lifetime lifetime) { }
                }
            }
            namespace Game.Services {
                public interface IFoo { }
                public class Foo : IFoo { }
            }
            namespace Game {
                using VContainer;
                using Game.Services;
                public class Installer {
                    void Configure(IContainerBuilder builder) {
                        builder.Register<IFoo, Foo>(Lifetime.Singleton);
                    }
                }
            }
            """;
        var reg = Assert.Single(AnalyzeSemantic(code));
        Assert.Equal("IFoo", reg.ServiceType);
        Assert.Equal("Foo", reg.ImplementationType);
        Assert.Equal("Game.Services.IFoo", reg.ServiceTypeQualified);
        Assert.Equal("Game.Services.Foo", reg.ImplementationTypeQualified);
    }

    [Fact]
    public void VContainer_Register_Syntactic_FullyQualified_PopulatesQualifiedNames()
    {
        var code = """
            enum Lifetime { Singleton }
            class Installer {
                void Configure(object builder) {
                    builder.Register<Game.Services.IFoo, Game.Services.Foo>(Lifetime.Singleton);
                }
            }
            """;
        var reg = Assert.Single(AnalyzeSyntactic(code));
        // Simple name is normalized (namespace stripped) to match ITypeSymbol.Name.
        Assert.Equal("IFoo", reg.ServiceType);
        Assert.Equal("Foo", reg.ImplementationType);
        Assert.Equal("Game.Services.IFoo", reg.ServiceTypeQualified);
        Assert.Equal("Game.Services.Foo", reg.ImplementationTypeQualified);
    }

    [Fact]
    public void VContainer_Register_Syntactic_SimpleName_NoQualifiedName()
    {
        var code = """
            enum Lifetime { Singleton }
            class IFoo { }
            class Foo { }
            class Installer {
                void Configure(object builder) {
                    builder.Register<IFoo, Foo>(Lifetime.Singleton);
                }
            }
            """;
        var reg = Assert.Single(AnalyzeSyntactic(code));
        Assert.Equal("IFoo", reg.ServiceType);
        Assert.Equal("Foo", reg.ImplementationType);
        // Bare simple names carry no qualification.
        Assert.Null(reg.ServiceTypeQualified);
        Assert.Null(reg.ImplementationTypeQualified);
    }

    [Fact]
    public void Zenject_BindTo_Semantic_PopulatesQualifiedNames()
    {
        // A Zenject stub binds container.Bind<T>(), so the service qualified name comes
        // from the resolved type symbol (bare name via `using`). The .To<T>() chain is
        // walked syntactically, so the impl qualified name still requires a fully
        // qualified write.
        var code = """
            namespace Zenject {
                public interface IBindChain { }
                public interface IFromChain { IFromChain AsSingle(); }
                public static class Binder {
                    public static IBindChain Bind<TService>(this object container) => null;
                    public static IFromChain To<TImpl>(this IBindChain chain) => null;
                }
            }
            namespace Game.Services {
                public interface IFoo { }
                public class Foo : IFoo { }
            }
            namespace Game {
                using Zenject;
                using Game.Services;
                public class Installer {
                    void Configure(object container) {
                        container.Bind<IFoo>().To<Game.Services.Foo>().AsSingle();
                    }
                }
            }
            """;
        var reg = Assert.Single(AnalyzeSemantic(code));
        Assert.Equal("IFoo", reg.ServiceType);
        Assert.Equal("Foo", reg.ImplementationType);
        // Bind<IFoo>() resolves semantically: namespace recovered from the type symbol.
        Assert.Equal("Game.Services.IFoo", reg.ServiceTypeQualified);
        // The .To<T>() chain is syntactic; the fully qualified write surfaces the impl qualified name.
        Assert.Equal("Game.Services.Foo", reg.ImplementationTypeQualified);
    }
}
