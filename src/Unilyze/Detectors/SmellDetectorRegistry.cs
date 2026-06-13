namespace Unilyze.Detectors;

internal static class SmellDetectorRegistry
{
    public static IReadOnlyList<ISmellDetector> All { get; } =
    [
        new BoxingSmellDetector(),
        new ClosureSmellDetector(),
        new ParamsSmellDetector(),
        new ExceptionFlowSmellDetector(),
        new AsyncFlowSmellDetector(),
        new WeakTemporizationSmellDetector(),
        new ExpensiveUnityApiInHotPathDetector(),
        new LinqInHotPathDetector(),
        new CollectionAllocationInHotPathDetector(),
        new StringConcatenationInHotPathDetector(),
        new EcsBurstSmellDetector(),
        new ManagedComponentDataSmellDetector(),
    ];
}
