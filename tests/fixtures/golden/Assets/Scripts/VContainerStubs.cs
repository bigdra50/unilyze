namespace VContainer;

public interface IContainerBuilder { }

public static class ContainerBuilderExtensions
{
    public static void Register<TInterface, TImplementation>(this IContainerBuilder builder) { }
    public static void Register<T>(this IContainerBuilder builder) { }
}
