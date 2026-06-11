using System.Diagnostics;

namespace Unilyze;

internal sealed class GitWorktreeException(string message) : Exception(message);

internal sealed class GitWorktreeSession : IDisposable
{
    public string RepoRoot { get; }
    public string WorktreePath { get; }

    bool _disposed;

    GitWorktreeSession(string repoRoot, string worktreePath)
    {
        RepoRoot = repoRoot;
        WorktreePath = worktreePath;
    }

    public static string GetRepoRelativePath(string projectPath)
    {
        var startPath = Path.GetFullPath(projectPath);
        if (!Directory.Exists(startPath) && !File.Exists(startPath))
            startPath = Path.GetDirectoryName(startPath) ?? startPath;

        return RunGit(startPath, "rev-parse", "--show-prefix").TrimEnd('/', '\\');
    }

    public static GitWorktreeSession Create(string startDirectory, string gitRef)
    {
        var startPath = Path.GetFullPath(startDirectory);
        if (!Directory.Exists(startPath) && !File.Exists(startPath))
            startPath = Path.GetDirectoryName(startPath) ?? startPath;

        string repoRoot;
        try
        {
            repoRoot = Path.GetFullPath(RunGit(startPath, "rev-parse", "--show-toplevel"));
        }
        catch (GitWorktreeException)
        {
            throw new GitWorktreeException("Not a git repository.");
        }

        try
        {
            RunGit(repoRoot, "rev-parse", "--verify", gitRef);
        }
        catch (GitWorktreeException)
        {
            throw new GitWorktreeException(
                $"Unknown git ref '{gitRef}'. Try 'git fetch origin <branch>' or use fetch-depth: 0 in checkout.");
        }

        var worktreePath = Path.Combine(
            Path.GetTempPath(),
            $"unilyze-worktree-{Guid.NewGuid():N}");

        RunGit(repoRoot, "worktree", "add", "--detach", worktreePath, gitRef);
        return new GitWorktreeSession(repoRoot, Path.GetFullPath(worktreePath));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (Directory.Exists(WorktreePath))
                RunGit(RepoRoot, "worktree", "remove", "--force", WorktreePath);
        }
        catch (GitWorktreeException)
        {
            // Best-effort cleanup; prune may still recover stale entries.
        }

        try
        {
            RunGit(RepoRoot, "worktree", "prune");
        }
        catch (GitWorktreeException)
        {
            // Ignore prune failures during teardown.
        }

        if (Directory.Exists(WorktreePath))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(WorktreePath, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(WorktreePath, true);
            }
            catch
            {
                // Directory may already be removed by git worktree remove.
            }
        }
    }

    static string RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
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
                      ?? throw new GitWorktreeException("Failed to start git process.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new GitWorktreeException("git executable not found.");
        }

        using (process)
        {
            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? $"exit {process.ExitCode}" : stderr.Trim();
                throw new GitWorktreeException(detail);
            }

            return stdout.TrimEnd();
        }
    }
}
