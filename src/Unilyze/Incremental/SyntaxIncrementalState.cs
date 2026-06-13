namespace Unilyze.Incremental;

internal static class SyntaxIncrementalState
{
    [ThreadStatic]
    static SyntaxIncrementalCollectResult? _current;

    public static SyntaxIncrementalCollectResult? Current
    {
        get => _current;
        set => _current = value;
    }
}
