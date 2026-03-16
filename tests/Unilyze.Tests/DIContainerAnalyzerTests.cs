using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests;

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
}
