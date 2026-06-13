namespace Unilyze.Serve;

internal sealed record ServeAnalysisFailure(string Code, string Summary);

internal sealed class ServeAnalysisException : Exception
{
    public ServeAnalysisException(string code, string summary, string detail)
        : base(detail) =>
        Failure = new ServeAnalysisFailure(code, summary);

    public ServeAnalysisFailure Failure { get; }
}

internal static class ServeAnalysisFailureClassifier
{
    public static ServeAnalysisFailure Classify(Exception exception) => exception switch
    {
        ServeAnalysisException serveException => serveException.Failure,
        UnauthorizedAccessException => new(
            "ANALYSIS_ACCESS_DENIED",
            "Analysis could not read one or more inputs."),
        IOException => new(
            "ANALYSIS_INPUT_UNAVAILABLE",
            "Analysis inputs changed or became unavailable."),
        _ => new(
            "ANALYSIS_FAILED",
            "Analysis failed. See the server log for details."),
    };
}
