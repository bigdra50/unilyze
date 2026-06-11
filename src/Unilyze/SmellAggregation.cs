namespace Unilyze;

internal static class SmellAggregation
{
    internal static bool CountsForGate(CodeSmell smell, bool excludeBaselined)
    {
        if (excludeBaselined && smell.Baselined == true)
            return false;
        if (TriageVerdicts.ExcludesFromGates(smell.Triage))
            return false;
        return true;
    }

    internal static bool CountsForTrend(CodeSmell smell)
        => !TriageVerdicts.ExcludesFromTrend(smell.Triage);

    internal static bool CountsForDiff(CodeSmell smell)
    {
        if (smell.Id is null)
            return true;
        return !TriageVerdicts.ExcludesFromGates(smell.Triage);
    }
}
