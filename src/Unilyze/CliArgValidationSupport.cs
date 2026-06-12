namespace Unilyze;

internal static class CliArgValidationSupport
{
    public static bool IsHelpRequest(string[] args) =>
        args.Any(arg => arg is "-h" or "--help");

    public static int ReportUnknown(string kind, string token, IEnumerable<string> candidates)
    {
        var message = $"Unknown {kind}: '{token}'";
        var suggestion = FindClosestMatch(token, candidates);
        if (suggestion is not null)
            message += $". Did you mean '{suggestion}'?";
        Console.Error.WriteLine(message);
        return 1;
    }

    public static string? FindUnknownOption(
        string[] args,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> booleanOptions)
    {
        var knownOptions = new HashSet<string>(valueOptions, StringComparer.Ordinal);
        knownOptions.UnionWith(booleanOptions);
        return ScanUnknownOption(args, valueOptions, knownOptions);
    }

    static string? ScanUnknownOption(
        string[] args,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> knownOptions)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (!option.StartsWith('-'))
                continue;
            if (!knownOptions.Contains(option))
                return option;
            if (valueOptions.Contains(option))
                i++;
        }

        return null;
    }

    public static List<string> ExtractPositionalArgs(
        string[] args,
        IReadOnlySet<string> valueOptions)
    {
        var positionals = new List<string>();
        for (var i = 0; i < args.Length; i++)
            i += AppendPositionalArg(args[i], valueOptions, positionals);
        return positionals;
    }

    static int AppendPositionalArg(
        string arg,
        IReadOnlySet<string> valueOptions,
        ICollection<string> positionals)
    {
        if (!arg.StartsWith('-'))
        {
            positionals.Add(arg);
            return 0;
        }

        return valueOptions.Contains(arg) ? 1 : 0;
    }

    public static int ValidateSubcommand(string subcommand, IEnumerable<string> candidates) =>
        candidates.Contains(subcommand)
            ? 0
            : ReportUnknown("subcommand", subcommand, candidates);

    public static int ValidateOptionsUnlessHelp(
        string[] args,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> booleanOptions) =>
        IsHelpRequest(args) ? 0 : ValidateOptions(args, valueOptions, booleanOptions);

    public static int ValidateOptionsAndPositionals(
        string[] args,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> booleanOptions,
        string command)
    {
        if (IsHelpRequest(args))
            return 0;

        var optionError = ValidateOptions(args, valueOptions, booleanOptions);
        if (optionError != 0)
            return optionError;

        var extra = FindUnexpectedPositional(args, valueOptions);
        return extra is null ? 0 : ReportUnknown("subcommand", extra, [command]);
    }

    public static int ValidateOptions(
        string[] args,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> booleanOptions)
    {
        var unknown = FindUnknownOption(args, valueOptions, booleanOptions);
        return unknown is null
            ? 0
            : ReportUnknown("option", unknown, valueOptions.Concat(booleanOptions));
    }

    static string? FindUnexpectedPositional(
        string[] args,
        IReadOnlySet<string> valueOptions)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (!option.StartsWith('-'))
                return option;
            if (valueOptions.Contains(option))
                i++;
        }

        return null;
    }

    static string? FindClosestMatch(string token, IEnumerable<string> candidates, int maxDistance = 2)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = LevenshteinDistance(token, candidate);
            if (distance > maxDistance || distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = candidate;
        }

        return best;
    }

    static int LevenshteinDistance(string source, string target)
    {
        if (source.Length == 0)
            return target.Length;
        if (target.Length == 0)
            return source.Length;

        var previous = Enumerable.Range(0, target.Length + 1).ToArray();
        var current = new int[target.Length + 1];

        for (var i = 1; i <= source.Length; i++)
        {
            CalculateDistanceRow(source[i - 1], target, i, previous, current);
            (previous, current) = (current, previous);
        }

        return previous[target.Length];
    }

    static void CalculateDistanceRow(
        char source,
        string target,
        int sourceIndex,
        IReadOnlyList<int> previous,
        IList<int> current)
    {
        current[0] = sourceIndex;
        for (var j = 1; j <= target.Length; j++)
            current[j] = CalculateDistance(source, target[j - 1], j, previous, current);
    }

    static int CalculateDistance(
        char source,
        char target,
        int targetIndex,
        IReadOnlyList<int> previous,
        IList<int> current)
    {
        var substitutionCost = source == target ? 0 : 1;
        return Math.Min(
            Math.Min(current[targetIndex - 1] + 1, previous[targetIndex] + 1),
            previous[targetIndex - 1] + substitutionCost);
    }
}
