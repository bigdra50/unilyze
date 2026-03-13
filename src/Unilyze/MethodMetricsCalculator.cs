using Microsoft.CodeAnalysis;

namespace Unilyze;

internal static class MethodMetricsCalculator
{
    public static (int CogCC, int CycCC, int NestDepth, double? HalsteadVolume) Calculate(SyntaxNode? bodyNode)
    {
        if (bodyNode is null)
            return (0, 1, 0, HalsteadCalculator.Calculate(null).Volume);

        var cogCC = CognitiveComplexity.Calculate(bodyNode);
        var cycCC = CyclomaticComplexity.Calculate(bodyNode);
        var nestDepth = NestingDepth.Calculate(bodyNode);
        var halstead = HalsteadCalculator.Calculate(bodyNode);
        return (cogCC, cycCC, nestDepth, halstead.Volume);
    }
}
