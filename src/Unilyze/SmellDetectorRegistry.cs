namespace Unilyze;

public static class SmellDetectorRegistry
{
    public static IReadOnlyList<ISmellDetector> All { get; } =
    [
        new BoxingSmellDetector(),
        new ClosureSmellDetector(),
        new ParamsSmellDetector(),
        new ExceptionFlowSmellDetector(),
        new WeakTemporizationSmellDetector(),
        new ExpensiveUnityApiInHotPathDetector(),
        new LinqInHotPathDetector(),
        new CollectionAllocationInHotPathDetector(),
        new StringConcatenationInHotPathDetector(),
    ];
}
