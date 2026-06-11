namespace Unilyze;

internal static class MultiProjectRunner
{
    public static int RunAnalyze(AnalyzeRunContext context)
        => MultiProjectAnalyzeRunner.Run(context);

    public static int RunBadge(string[] args, IReadOnlyDictionary<string, string> opts, IReadOnlyList<string> projectGlobs)
        => MultiProjectBadgeRunner.Run(args, opts, projectGlobs);
}
