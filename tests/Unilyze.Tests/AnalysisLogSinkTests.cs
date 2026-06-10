namespace Unilyze.Tests;

public sealed class AnalysisLogSinkTests
{
    [Fact]
    public void QuietSink_SuppressesInfo_KeepsWarning()
    {
        var originalError = Console.Error;
        using var capture = new StringWriter();
        try
        {
            Console.SetError(capture);
            var sink = new ConsoleAnalysisLogSink(quiet: true);

            sink.Info("info-line");
            sink.Warning("warn-line");

            var output = capture.ToString();
            Assert.DoesNotContain("info-line", output);
            Assert.Contains("warn-line", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void NonQuietSink_WritesInfo()
    {
        var originalError = Console.Error;
        using var capture = new StringWriter();
        try
        {
            Console.SetError(capture);
            var sink = new ConsoleAnalysisLogSink(quiet: false);

            sink.Info("info-line");

            Assert.Contains("info-line", capture.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void RedirectedStderr_EmitsNoPhaseProgress_QuietOrNot()
    {
        var originalError = Console.Error;
        using var capture = new StringWriter();
        try
        {
            Console.SetError(capture);

            foreach (var quiet in new[] { false, true })
            {
                capture.GetStringBuilder().Clear();
                var sink = new ConsoleAnalysisLogSink(quiet: quiet);
                sink.PhaseStarted("discover");
                sink.PhaseCompleted("discover", TimeSpan.FromSeconds(1.2));

                Assert.DoesNotContain(" done ", capture.ToString());
            }
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void NullSink_IsSilent()
    {
        var sink = NullAnalysisLogSink.Null;
        sink.Info("info");
        sink.Warning("warn");
        sink.PhaseStarted("discover");
        sink.PhaseCompleted("discover", TimeSpan.FromSeconds(1));

        Assert.True(true);
    }
}
