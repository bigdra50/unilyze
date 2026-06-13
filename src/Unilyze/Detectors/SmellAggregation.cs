using Unilyze.Findings;
namespace Unilyze.Detectors;

internal static class SmellAggregation
{
    internal static bool CountsForGate(CodeSmell smell, bool excludeBaselined)
    {
        if (smell.Suppressed == true)
            return false;
        if (excludeBaselined && smell.Baselined == true)
            return false;
        if (TriageVerdicts.ExcludesFromGates(smell.Triage))
            return false;
        return true;
    }

    internal static bool CountsForTrend(CodeSmell smell)
        => smell.Suppressed != true && !TriageVerdicts.ExcludesFromTrend(smell.Triage);

    internal static bool CountsForDiff(CodeSmell smell)
    {
        if (smell.Suppressed == true)
            return false;
        if (smell.Id is null)
            return true;
        return !TriageVerdicts.ExcludesFromGates(smell.Triage);
    }
}
