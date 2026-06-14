using System.Diagnostics;
using System.Text;

namespace Unilyze.Serve;

internal enum GitDiffState
{
    Normal,
    Untracked,
    Deleted,
    UnbornHead,
    NoRepo,
}

internal sealed record GitDiffResult(GitDiffState State, string? Diff);

internal sealed class GitDiffService
{
    const int MaxDiffBytes = 512 * 1024;
    const int TimeoutMs = 10_000;

    readonly string _projectRoot;

    public GitDiffService(string projectRoot) => _projectRoot = projectRoot;

    public GitDiffResult GetFileDiff(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (!IsGitRepo())
            return new GitDiffResult(GitDiffState.NoRepo, null);

        if (!HasHead())
            return new GitDiffResult(GitDiffState.UnbornHead, ReadFileAsAddedDiff(absolutePath));

        var relativePath = GetRelativePath(absolutePath);

        if (IsUntracked(relativePath))
            return new GitDiffResult(GitDiffState.Untracked, ReadFileAsAddedDiff(absolutePath));

        if (!File.Exists(absolutePath))
            return new GitDiffResult(GitDiffState.Deleted, RunGit(cancellationToken, "diff", "HEAD", "--", relativePath));

        var diff = RunGit(cancellationToken, "diff", "HEAD", "--", relativePath);
        return new GitDiffResult(GitDiffState.Normal, string.IsNullOrEmpty(diff) ? null : diff);
    }

    bool IsGitRepo()
    {
        try
        {
            RunGit(default, "rev-parse", "--is-inside-work-tree");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    bool HasHead()
    {
        try
        {
            RunGit(default, "rev-parse", "--verify", "HEAD");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    bool IsUntracked(string relativePath)
    {
        var output = RunGit(default, "ls-files", "--others", "--exclude-standard", "--", relativePath);
        return !string.IsNullOrWhiteSpace(output);
    }

    string GetRelativePath(string absolutePath)
    {
        var fullRoot = Path.GetFullPath(_projectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(absolutePath);
        return fullPath.StartsWith(fullRoot, StringComparison.Ordinal)
            ? fullPath[fullRoot.Length..].Replace('\\', '/')
            : Path.GetFileName(absolutePath);
    }

    static string? ReadFileAsAddedDiff(string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return null;

        try
        {
            var lines = File.ReadAllLines(absolutePath);
            var sb = new StringBuilder();
            sb.AppendLine($"--- /dev/null");
            sb.AppendLine($"+++ b/{Path.GetFileName(absolutePath)}");
            sb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
            foreach (var line in lines)
                sb.AppendLine($"+{line}");
            return sb.ToString();
        }
        catch (IOException)
        {
            return null;
        }
    }

    string RunGit(CancellationToken cancellationToken, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(psi)
                      ?? throw new InvalidOperationException("Failed to start git process.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("git executable not found.");
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            if (!process.WaitForExit(TimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException("git diff timed out.");
            }

            Task.WaitAll([stdoutTask, stderrTask], cancellationToken);
            var stdout = stdoutTask.Result;

            if (stdout.Length > MaxDiffBytes)
                stdout = stdout[..MaxDiffBytes] + "\n... (truncated)";

            if (process.ExitCode != 0)
            {
                var stderr = stderrTask.Result;
                var detail = string.IsNullOrWhiteSpace(stderr) ? $"exit {process.ExitCode}" : stderr.Trim();
                throw new InvalidOperationException($"git failed: {detail}");
            }

            return stdout;
        }
    }
}
