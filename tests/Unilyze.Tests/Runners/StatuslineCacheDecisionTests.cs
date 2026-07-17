namespace Unilyze.Tests.Runners;

public sealed class StatuslineCacheDecisionTests
{
    [Fact]
    public void Decide_NoCache_RefreshesInBackgroundWithNoOutput()
    {
        var action = StatuslineCacheDecision.Decide(cacheExists: false, cacheAgeSeconds: null, refreshSeconds: 60);
        Assert.Equal(StatuslineCacheAction.RefreshOnly, action);
    }

    [Fact]
    public void Decide_FreshCache_ServesWithoutRefreshing()
    {
        var action = StatuslineCacheDecision.Decide(cacheExists: true, cacheAgeSeconds: 10, refreshSeconds: 60);
        Assert.Equal(StatuslineCacheAction.ServeFresh, action);
    }

    [Fact]
    public void Decide_StaleCache_ServesStaleAndRefreshes()
    {
        var action = StatuslineCacheDecision.Decide(cacheExists: true, cacheAgeSeconds: 120, refreshSeconds: 60);
        Assert.Equal(StatuslineCacheAction.ServeStaleAndRefresh, action);
    }

    [Fact]
    public void Decide_AgeEqualToTtl_IsTreatedAsStale()
    {
        var action = StatuslineCacheDecision.Decide(cacheExists: true, cacheAgeSeconds: 60, refreshSeconds: 60);
        Assert.Equal(StatuslineCacheAction.ServeStaleAndRefresh, action);
    }

    [Fact]
    public void Decide_ZeroTtl_AlwaysRefreshesWhenCachePresent()
    {
        var action = StatuslineCacheDecision.Decide(cacheExists: true, cacheAgeSeconds: 0, refreshSeconds: 0);
        Assert.Equal(StatuslineCacheAction.ServeStaleAndRefresh, action);
    }

    [Fact]
    public void Decide_CacheExistsButAgeUnknown_IsTreatedAsStale()
    {
        var action = StatuslineCacheDecision.Decide(cacheExists: true, cacheAgeSeconds: null, refreshSeconds: 60);
        Assert.Equal(StatuslineCacheAction.ServeStaleAndRefresh, action);
    }
}
