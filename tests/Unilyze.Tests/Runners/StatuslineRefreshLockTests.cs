using System.Diagnostics;

namespace Unilyze.Tests.Runners;

public sealed class StatuslineRefreshLockTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    // A pid no platform hands out: Linux caps at 4194304, macOS at 99998, Windows pids are
    // multiples of 4 well below this.
    const int NeverRunningPid = int.MaxValue;

    readonly string _tempDir;

    public StatuslineRefreshLockTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_RefreshLock_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    string LockPath => Path.Combine(_tempDir, "unilyze-sl-test.lock");

    static ProcessLiveness Running(DateTimeOffset? startedAt) => new(IsRunning: true, StartTime: startedAt);
    static ProcessLiveness NotRunning() => new(IsRunning: false, StartTime: null);

    [Fact]
    public void ParseHolder_StampFromCurrentFormat_ReturnsPidAndAcquisitionTime()
    {
        var holder = StatuslineRefreshLock.ParseHolder("4242 2026-10-23T07:59:30.0000000+00:00\n");

        Assert.NotNull(holder);
        Assert.Equal(4242, holder.Pid);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 7, 59, 30, TimeSpan.Zero), holder.AcquiredAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("not-a-pid 2026-10-23T07:59:30.0000000+00:00")]
    [InlineData("4242 not-a-timestamp")]
    [InlineData("4242")]
    public void ParseHolder_UnreadableStamp_ReturnsNull(string content)
    {
        Assert.Null(StatuslineRefreshLock.ParseHolder(content));
    }

    [Fact]
    public void IsAbandoned_HolderNotRunning_IsReclaimableImmediately()
    {
        var holder = new StatuslineLockHolder(4242, Now.AddSeconds(-1));

        var abandoned = StatuslineRefreshLock.IsAbandoned(holder, lockWrittenAt: Now.AddSeconds(-1), Now, _ => NotRunning());

        Assert.True(abandoned);
    }

    [Fact]
    public void IsAbandoned_HolderStillRunning_IsNotReclaimableHoweverOldTheLockIs()
    {
        var acquiredAt = Now.AddHours(-2);
        var holder = new StatuslineLockHolder(4242, acquiredAt);

        var abandoned = StatuslineRefreshLock.IsAbandoned(
            holder, lockWrittenAt: acquiredAt, Now, _ => Running(acquiredAt.AddMinutes(-1)));

        Assert.False(abandoned);
    }

    [Fact]
    public void IsAbandoned_PidReusedByProcessStartedAfterTheStamp_IsReclaimable()
    {
        var acquiredAt = Now.AddMinutes(-10);
        var holder = new StatuslineLockHolder(4242, acquiredAt);

        var abandoned = StatuslineRefreshLock.IsAbandoned(
            holder, lockWrittenAt: acquiredAt, Now, _ => Running(acquiredAt.AddMinutes(5)));

        Assert.True(abandoned);
    }

    [Fact]
    public void IsAbandoned_StartTimeWithinSlackAfterTheStamp_IsStillTheHolder()
    {
        var acquiredAt = Now.AddMinutes(-10);
        var holder = new StatuslineLockHolder(4242, acquiredAt);

        var abandoned = StatuslineRefreshLock.IsAbandoned(
            holder, lockWrittenAt: acquiredAt, Now, _ => Running(acquiredAt.AddSeconds(1)));

        Assert.False(abandoned);
    }

    [Fact]
    public void IsAbandoned_HolderRunningWithUnknownStartTime_IsNotReclaimable()
    {
        var holder = new StatuslineLockHolder(4242, Now.AddHours(-2));

        var abandoned = StatuslineRefreshLock.IsAbandoned(
            holder, lockWrittenAt: Now.AddHours(-2), Now, _ => Running(null));

        Assert.False(abandoned);
    }

    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    public void IsAbandoned_UnreadableStamp_FallsBackToLockAge(int ageSeconds, bool expected)
    {
        var abandoned = StatuslineRefreshLock.IsAbandoned(
            holder: null, lockWrittenAt: Now.AddSeconds(-ageSeconds), Now, _ => throw new InvalidOperationException("probe must not be consulted"));

        Assert.Equal(expected, abandoned);
    }

    [Fact]
    public void TryAcquire_NoLock_CreatesLockStampedWithOwnPid()
    {
        var acquired = StatuslineRefreshLock.TryAcquire(LockPath, out var lockStream);

        try
        {
            Assert.True(acquired);
            var holder = StatuslineRefreshLock.ParseHolder(ReadLockWhileHeld());
            Assert.NotNull(holder);
            Assert.Equal(Environment.ProcessId, holder.Pid);
        }
        finally
        {
            StatuslineRefreshLock.Release(lockStream, LockPath);
        }

        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public void TryAcquire_HeldByLiveProcess_FailsEvenWhenTheFileIsOlderThanTwoMinutes()
    {
        WriteLock(Environment.ProcessId, CurrentProcessStartTime(), fileAge: TimeSpan.FromHours(2));

        var acquired = StatuslineRefreshLock.TryAcquire(LockPath, out var lockStream);

        try
        {
            Assert.False(acquired);
            Assert.True(File.Exists(LockPath));
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_HeldByDeadProcess_ReclaimsEvenWhenTheFileIsFresh()
    {
        WriteLock(NeverRunningPid, DateTimeOffset.UtcNow, fileAge: TimeSpan.Zero);

        var acquired = StatuslineRefreshLock.TryAcquire(LockPath, out var lockStream);

        try
        {
            Assert.True(acquired);
            var holder = StatuslineRefreshLock.ParseHolder(ReadLockWhileHeld());
            Assert.NotNull(holder);
            Assert.Equal(Environment.ProcessId, holder.Pid);
        }
        finally
        {
            StatuslineRefreshLock.Release(lockStream, LockPath);
        }
    }

    [Theory]
    [InlineData(60, false)]
    [InlineData(180, true)]
    public void TryAcquire_UnreadableLock_FallsBackToFileAge(int ageSeconds, bool expectedAcquired)
    {
        File.WriteAllText(LockPath, "garbage");
        File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow.AddSeconds(-ageSeconds));

        var acquired = StatuslineRefreshLock.TryAcquire(LockPath, out var lockStream);

        try
        {
            Assert.Equal(expectedAcquired, acquired);
        }
        finally
        {
            StatuslineRefreshLock.Release(lockStream, LockPath);
        }
    }

    [Fact]
    public void IsHeldByLiveProcess_NoLock_IsFalse()
    {
        Assert.False(StatuslineRefreshLock.IsHeldByLiveProcess(LockPath, out var holder));
        Assert.Null(holder);
    }

    [Fact]
    public void IsHeldByLiveProcess_LiveHolder_ReportsTheHolder()
    {
        WriteLock(Environment.ProcessId, CurrentProcessStartTime(), fileAge: TimeSpan.FromHours(2));

        Assert.True(StatuslineRefreshLock.IsHeldByLiveProcess(LockPath, out var holder));
        Assert.NotNull(holder);
        Assert.Equal(Environment.ProcessId, holder.Pid);
    }

    [Fact]
    public void IsHeldByLiveProcess_DeadHolder_IsFalseAndLeavesTheFileForTheNextAcquirer()
    {
        WriteLock(NeverRunningPid, DateTimeOffset.UtcNow, fileAge: TimeSpan.Zero);

        Assert.False(StatuslineRefreshLock.IsHeldByLiveProcess(LockPath, out _));
        Assert.True(File.Exists(LockPath));
    }

    void WriteLock(int pid, DateTimeOffset acquiredAt, TimeSpan fileAge)
    {
        File.WriteAllText(LockPath, $"{pid} {acquiredAt:O}\n");
        File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow - fileAge);
    }

    string ReadLockWhileHeld()
    {
        using var stream = new FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // The stamp a long-lived holder would carry: as old as this process itself, so the
    // "pid reused" rule (start time later than the stamp) does not fire for our own pid.
    static DateTimeOffset CurrentProcessStartTime()
    {
        using var current = Process.GetCurrentProcess();
        return new DateTimeOffset(current.StartTime.ToUniversalTime());
    }
}
