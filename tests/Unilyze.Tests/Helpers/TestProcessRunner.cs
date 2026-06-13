using System.Diagnostics;

namespace Unilyze.Tests.Helpers;

internal static class TestProcessRunner
{
    internal static (int ExitCode, string StdOut, string StdErr) Run(
        ProcessStartInfo psi,
        int timeoutMs = 120_000,
        string? processLabel = null)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {psi.FileName}");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(timeoutMs))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup; the timeout assertion below must still report diagnostics.
            }

            string stderr;
            try
            {
                stderr = stderrTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                stderr = "<stderr drain did not complete after termination>";
            }

            Assert.Fail($"{processLabel ?? psi.FileName} timed out after {timeoutMs} ms. stderr: {stderr}");
        }

        return (proc.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    internal static (int ExitCode, string StdOut, string StdErr) RunWithStdin(
        ProcessStartInfo psi,
        string stdinContent,
        int timeoutMs = 120_000,
        string? processLabel = null)
    {
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {psi.FileName}");

        proc.StandardInput.Write(stdinContent);
        proc.StandardInput.Close();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(timeoutMs))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup; the timeout assertion below must still report diagnostics.
            }

            string stderr;
            try
            {
                stderr = stderrTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                stderr = "<stderr drain did not complete after termination>";
            }

            Assert.Fail($"{processLabel ?? psi.FileName} timed out after {timeoutMs} ms. stderr: {stderr}");
        }

        return (proc.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }
}
