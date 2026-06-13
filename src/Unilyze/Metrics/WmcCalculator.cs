using Unilyze.Pipeline;
namespace Unilyze.Metrics;

internal static class WmcCalculator
{
    public static int Calculate(IReadOnlyList<MemberInfo> members)
    {
        var sum = 0;
        foreach (var member in members)
        {
            if (member.CyclomaticComplexity is { } cc)
                sum += cc;
        }
        return sum;
    }
}
