namespace Unilyze;

internal sealed record HotspotAnalysisContext(
    string ProjectPath,
    string Since,
    int TopN,
    bool BotFilter,
    int BotCommitsExcluded,
    string? HalfLife,
    TimeSpan? HalfLifeSpan);
