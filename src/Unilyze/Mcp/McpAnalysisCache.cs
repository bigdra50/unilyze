using Unilyze.Runners;
using Unilyze.Pipeline;
namespace Unilyze.Mcp;

internal sealed class McpAnalysisCache
{
    AnalysisResult? _cached;
    string? _cacheKey;

    public AnalysisResult Load(McpToolArgs args, bool includeApiSurface = false)
    {
        var key = BuildKey(args, includeApiSurface);
        if (_cached is not null && _cacheKey == key)
            return _cached;

        _cached = QueryRunner.LoadAnalysis(args.Input, args.PathOrDefault(), [], includeApiSurface);
        _cacheKey = key;
        return _cached;
    }

    public void Store(AnalysisResult result, McpToolArgs args, bool includeApiSurface = false)
    {
        _cached = result;
        _cacheKey = BuildKey(args, includeApiSurface);
    }

    static string BuildKey(McpToolArgs args, bool includeApiSurface) =>
        $"{args.Input ?? Path.GetFullPath(args.PathOrDefault())}|api={includeApiSurface}";
}
