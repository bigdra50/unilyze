namespace Unilyze.Runners;

/// <summary>
/// What a non-blocking <c>statusline</c> invocation should do given the current
/// cache state. The subcommand never runs analysis in the foreground; it either
/// serves the cache or spawns a detached refresh.
/// </summary>
internal enum StatuslineCacheAction
{
    /// <summary>Cache is present and within the TTL: print it, do not refresh.</summary>
    ServeFresh,

    /// <summary>Cache is present but older than the TTL: print the stale line, then refresh in the background.</summary>
    ServeStaleAndRefresh,

    /// <summary>No cache yet: print nothing (hidden segment) and refresh in the background.</summary>
    RefreshOnly,
}

internal static class StatuslineCacheDecision
{
    /// <summary>
    /// Decides the stale-while-revalidate action. A missing age on an existing cache
    /// is treated as stale so a refresh is always scheduled rather than serving an
    /// indefinitely old line.
    /// </summary>
    internal static StatuslineCacheAction Decide(bool cacheExists, double? cacheAgeSeconds, int refreshSeconds)
    {
        if (!cacheExists)
            return StatuslineCacheAction.RefreshOnly;

        var age = cacheAgeSeconds ?? double.PositiveInfinity;
        return age >= refreshSeconds
            ? StatuslineCacheAction.ServeStaleAndRefresh
            : StatuslineCacheAction.ServeFresh;
    }
}
