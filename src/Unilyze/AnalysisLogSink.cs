namespace Unilyze;

internal interface IAnalysisLogSink
{
    void Info(string message);
    void Warning(string message);
    void PhaseStarted(string phase);
    void PhaseCompleted(string phase, TimeSpan elapsed);
}

internal sealed class ConsoleAnalysisLogSink : IAnalysisLogSink
{
    readonly bool _quiet;
    readonly bool _showProgress;

    public ConsoleAnalysisLogSink(bool quiet)
    {
        _quiet = quiet;
        _showProgress = !Console.IsErrorRedirected;
    }

    public void Info(string message)
    {
        if (_quiet) return;
        Console.Error.WriteLine(message);
    }

    public void Warning(string message)
    {
        Console.Error.WriteLine(message);
    }

    public void PhaseStarted(string phase)
    {
        if (!_showProgress || _quiet) return;
    }

    public void PhaseCompleted(string phase, TimeSpan elapsed)
    {
        if (!_showProgress || _quiet) return;
        Console.Error.WriteLine($"[{phase}] done {elapsed.TotalSeconds:F1}s");
    }
}

internal sealed class NullAnalysisLogSink : IAnalysisLogSink
{
    public static NullAnalysisLogSink Null { get; } = new();

    NullAnalysisLogSink() { }

    public void Info(string message) { }
    public void Warning(string message) { }
    public void PhaseStarted(string phase) { }
    public void PhaseCompleted(string phase, TimeSpan elapsed) { }
}
