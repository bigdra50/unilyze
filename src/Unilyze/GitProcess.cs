using System.Diagnostics;

namespace Unilyze;

internal static class GitProcess
{
    public static string Run(string workingDirectory, params string[] args)
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
                      ?? throw new InvalidOperationException("Failed to start git process.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("git executable not found.");
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(120_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup.
                }

                throw new InvalidOperationException("git timed out after 120s.");
            }

            Task.WaitAll(stdoutTask, stderrTask);
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? $"exit {process.ExitCode}" : stderr.Trim();
                throw new InvalidOperationException($"git failed: {detail}");
            }

            return stdout;
        }
    }
}
