using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Unilyze.Runners;

/// <summary>What the OS currently reports for a lock holder's pid.</summary>
internal readonly record struct ProcessLiveness(bool IsRunning, DateTimeOffset? StartTime);

/// <summary>The process that stamped the lock, and when it did.</summary>
internal sealed record StatuslineLockHolder(int Pid, DateTimeOffset AcquiredAt);

/// <summary>
/// Refresh lock next to the statusline cache. The file carries "&lt;pid&gt; &lt;ISO-8601&gt;"
/// (the stamp 0.6.1 already wrote) so another invocation can tell a refresh that is still
/// running from one whose process died. Liveness, not file age, decides whether the lock
/// is reclaimable: a large project refreshes for longer than any fixed timeout, and the
/// previous age rule let every later render reclaim the lock and start one more analysis
/// beside the ones still running.
/// </summary>
internal static class StatuslineRefreshLock
{
    // Process.StartTime on Linux derives from boot time at 1 s resolution, and the holder
    // stamps the lock only after it has started; a start time later than the stamp by more
    // than this belongs to a different process that reused the pid.
    static readonly TimeSpan StartTimeSlack = TimeSpan.FromSeconds(5);

    // Only for a lock whose stamp cannot be read (truncated file, or a 0.6.1 holder on
    // Windows that opened it without read sharing): keep the 0.6.1 age rule.
    static readonly TimeSpan UnreadableLockStaleAfter = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Creates the lock exclusively (atomic across processes). A lock left by a dead process
    /// is reclaimed once; a lock held by a live process is never taken over.
    /// </summary>
    public static bool TryAcquire(string lockPath, out FileStream? lockStream)
    {
        lockStream = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // Read sharing lets other invocations inspect the stamp while it is held.
                // Delete still fails on Windows while this stream is open, so a live holder's
                // lock cannot be reclaimed there even by a caller that misjudges liveness.
                lockStream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                var stamp = Encoding.UTF8.GetBytes($"{Environment.ProcessId} {DateTimeOffset.UtcNow:O}\n");
                lockStream.Write(stamp, 0, stamp.Length);
                lockStream.Flush();
                return true;
            }
            catch (IOException)
            {
                if (attempt == 0 && TryReclaimAbandonedLock(lockPath))
                    continue;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// True while a refresh process is alive and owns the lock. <paramref name="holder"/> is
    /// the stamp when it could be read, null for an unreadable lock that is still inside the
    /// age fallback. Never modifies the file.
    /// </summary>
    public static bool IsHeldByLiveProcess(string lockPath, out StatuslineLockHolder? holder)
    {
        holder = null;
        if (!File.Exists(lockPath))
            return false;

        holder = ParseHolder(TryReadStamp(lockPath));
        return !IsAbandoned(holder, LastWriteTime(lockPath), DateTimeOffset.UtcNow, Probe);
    }

    public static void Release(FileStream? lockStream, string lockPath)
    {
        try
        {
            lockStream?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // best-effort
        }

        try
        {
            File.Delete(lockPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    /// <summary>
    /// Decides whether a lock may be reclaimed. <paramref name="probe"/> answers for the
    /// holder's pid; a running holder whose start time is unknown is treated as alive, since
    /// the only cost of waiting is one more render without a refresh.
    /// </summary>
    internal static bool IsAbandoned(
        StatuslineLockHolder? holder,
        DateTimeOffset lockWrittenAt,
        DateTimeOffset now,
        Func<int, ProcessLiveness> probe)
    {
        if (holder is null)
            return now - lockWrittenAt >= UnreadableLockStaleAfter;

        var liveness = probe(holder.Pid);
        if (!liveness.IsRunning)
            return true;

        return liveness.StartTime is { } startedAt && startedAt > holder.AcquiredAt + StartTimeSlack;
    }

    internal static StatuslineLockHolder? ParseHolder(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var parts = content.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            return null;

        if (!DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var acquiredAt))
            return null;

        return new StatuslineLockHolder(pid, acquiredAt);
    }

    static bool TryReclaimAbandonedLock(string lockPath)
    {
        try
        {
            var holder = ParseHolder(TryReadStamp(lockPath));
            if (!IsAbandoned(holder, LastWriteTime(lockPath), DateTimeOffset.UtcNow, Probe))
                return false;

            File.Delete(lockPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A live holder keeps the file open on Windows; deletion fails -> not reclaimable.
            return false;
        }
    }

    static string? TryReadStamp(string lockPath)
    {
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static DateTimeOffset LastWriteTime(string lockPath) =>
        new(File.GetLastWriteTimeUtc(lockPath));

    static ProcessLiveness Probe(int pid)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return new ProcessLiveness(IsRunning: false, StartTime: null);
        }

        using (process)
        {
            try
            {
                return new ProcessLiveness(IsRunning: true, new DateTimeOffset(process.StartTime.ToUniversalTime()));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or IOException)
            {
                return new ProcessLiveness(IsRunning: true, StartTime: null);
            }
        }
    }
}
