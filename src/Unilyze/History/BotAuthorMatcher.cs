using System.Text.RegularExpressions;

namespace Unilyze.History;

internal sealed class BotAuthorMatcher
{
    static readonly string[] KnownBotNames =
    [
        "dependabot",
        "renovate",
        "github-actions",
        "greenkeeper",
        "snyk-bot",
        "imgbot",
        "allcontributors",
        "pre-commit-ci",
        "codecov",
    ];

    readonly List<Regex> _customPatterns = [];

    public static BotAuthorMatcher CreateDefault() => new();

    public void AddPattern(string pattern)
    {
        try
        {
            _customPatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Invalid bot pattern '{pattern}': {ex.Message}");
        }
    }

    public bool IsBot(string authorName, string authorEmail)
    {
        if (string.IsNullOrWhiteSpace(authorName))
            return false;

        var name = authorName.Trim();
        if (MatchesGitHubBotSuffix(name))
            return true;

        if (MatchesKnownBotName(name))
            return true;

        if (!string.IsNullOrWhiteSpace(authorEmail)
            && MatchesKnownBotEmail(authorEmail.Split('@')[0]))
            return true;

        return MatchesCustomPattern(name, authorEmail);
    }

    static bool MatchesGitHubBotSuffix(string name) =>
        name.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase);

    static bool MatchesKnownBotName(string name)
    {
        foreach (var known in KnownBotNames)
        {
            if (name.Equals(known, StringComparison.OrdinalIgnoreCase)
                || name.Equals(known + "[bot]", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static bool MatchesKnownBotEmail(string localPart)
    {
        if (localPart.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)
            || localPart.EndsWith("-bot", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var known in KnownBotNames)
        {
            if (localPart.Equals(known, StringComparison.OrdinalIgnoreCase)
                || localPart.Equals(known + "[bot]", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    bool MatchesCustomPattern(string name, string authorEmail)
    {
        foreach (var pattern in _customPatterns)
        {
            if (pattern.IsMatch(name))
                return true;
            if (!string.IsNullOrWhiteSpace(authorEmail) && pattern.IsMatch(authorEmail))
                return true;
        }

        return false;
    }
}
