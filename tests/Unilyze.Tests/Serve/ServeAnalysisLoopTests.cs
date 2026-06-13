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
                new Dictionary<string, string>(), new Dictionary<string, string>());
        }

        bool _throwNext;
        public void SetThrowNext() => Volatile.Write(ref _throwNext, true);
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
}
