using System.Diagnostics;
using System.Text;

namespace Unilyze.Tests.Serve;

public sealed class ServeAnalysisLoopTests
{
    sealed class CountingBuilder
    {
        int _count;
        public int Count => Volatile.Read(ref _count);

        public ServeSnapshotContent Build()
        {
            var n = Interlocked.Increment(ref _count);
            if (Volatile.Read(ref _throwNext))
            {
                Volatile.Write(ref _throwNext, false);
                throw new InvalidOperationException("boom");
            }
            var bytes = Encoding.UTF8.GetBytes($"{{\"n\":{n}}}");
            return new ServeSnapshotContent(
                bytes, $"\"etag-{n}\"", DateTimeOffset.UtcNow,
                new ServeAnalysisMetrics(1.0, bytes.Length),
                new Dictionary<string, string>(), new Dictionary<string, string>(), []);
        }

        bool _throwNext;
        public void SetThrowNext() => Volatile.Write(ref _throwNext, true);
    }

    sealed class BlockingBuilder
    {
        readonly ManualResetEventSlim _firstBuildStarted = new(false);
        readonly ManualResetEventSlim _releaseFirstBuild = new(false);
        int _count;

        public int Count => Volatile.Read(ref _count);

        public bool WaitForFirstBuild(TimeSpan timeout) => _firstBuildStarted.Wait(timeout);

        public void ReleaseFirstBuild() => _releaseFirstBuild.Set();

        public ServeSnapshotContent Build()
        {
            var n = Interlocked.Increment(ref _count);
            if (n == 1)
            {
                _firstBuildStarted.Set();
                _releaseFirstBuild.Wait(TimeSpan.FromSeconds(5));
            }

            var bytes = Encoding.UTF8.GetBytes($"{{\"n\":{n}}}");
            return new ServeSnapshotContent(
                bytes, $"\"etag-{n}\"", DateTimeOffset.UtcNow,
                new ServeAnalysisMetrics(1.0, bytes.Length),
                new Dictionary<string, string>(), new Dictionary<string, string>(), []);
        }
    }

    static bool WaitUntil(Func<bool> predicate, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate())
                return true;
            Thread.Sleep(15);
        }
        return predicate();
    }

    [Fact]
    public void Start_RunsInitialAnalysis_PublishesSnapshot()
    {
        var store = new SnapshotStore();
        var builder = new CountingBuilder();
        using var coordinator = new AnalysisCoordinator(store, builder.Build, TimeSpan.FromMilliseconds(30));

        coordinator.Start();

        Assert.True(WaitUntil(() => store.GetState().Phase == ServePhase.Ready && store.Current != null));
        Assert.Equal(1, builder.Count);
    }

    [Fact]
    public void RapidRequests_CoalesceIntoSingleReanalysis()
    {
        var store = new SnapshotStore();
        var builder = new CountingBuilder();
        using var coordinator = new AnalysisCoordinator(store, builder.Build, TimeSpan.FromMilliseconds(50));

        coordinator.Start();
        Assert.True(WaitUntil(() => store.Current != null));
        var genBefore = store.GetState().Generation;

        for (var i = 0; i < 25; i++)
            coordinator.RequestAnalysis();

        Assert.True(WaitUntil(() =>
            store.GetState().Generation > genBefore && store.GetState().Phase == ServePhase.Ready));

        // Initial run + exactly one coalesced run for the burst.
        Assert.Equal(2, builder.Count);
    }

    [Fact]
    public void FailedAnalysis_KeepsPreviousSnapshot_AndMarksStale()
    {
        var store = new SnapshotStore();
        var builder = new CountingBuilder();
        using var coordinator = new AnalysisCoordinator(store, builder.Build, TimeSpan.FromMilliseconds(30));

        coordinator.Start();
        Assert.True(WaitUntil(() => store.Current != null));
        var previous = store.Current;

        builder.SetThrowNext();
        coordinator.RequestAnalysis();

        Assert.True(WaitUntil(() => store.GetState().Phase == ServePhase.Failed));
        Assert.Same(previous, store.Current);
        Assert.NotNull(store.GetState().LastError);

        // Recovery: a subsequent successful run replaces the snapshot.
        coordinator.RequestAnalysis();
        Assert.True(WaitUntil(() =>
            store.GetState().Phase == ServePhase.Ready && !ReferenceEquals(store.Current, previous)));
    }

    [Fact]
    public async Task WaitForChangeAsync_WhenCancelled_StopsWaiting()
    {
        var store = new SnapshotStore();
        using var cancellation = new CancellationTokenSource();
        var wait = store.WaitForChangeAsync(
            store.GetState().Generation,
            TimeSpan.FromSeconds(25),
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public void ChangeDuringAnalysis_DiscardsOlderGeneration()
    {
        var store = new SnapshotStore();
        var builder = new BlockingBuilder();
        using var coordinator = new AnalysisCoordinator(
            store, builder.Build, TimeSpan.FromMilliseconds(20));

        coordinator.Start();
        Assert.True(builder.WaitForFirstBuild(TimeSpan.FromSeconds(3)));
        coordinator.RequestAnalysis();
        builder.ReleaseFirstBuild();

        Assert.True(WaitUntil(() => store.GetState().Phase == ServePhase.Ready));
        Assert.Equal(2, builder.Count);
        Assert.Equal("\"etag-2\"", store.Current?.ETag);
    }
}
