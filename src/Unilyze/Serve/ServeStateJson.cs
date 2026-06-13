using System.Text.Json;

namespace Unilyze.Serve;

/// <summary>
/// Serializes <see cref="ServeStateView"/> for <c>GET /api/state</c>. Written by hand
/// (rather than via the analysis source-gen context) because it is a tiny, serve-only
/// shape; <c>phase</c> is the camelCase enum name the viewer's status bar consumes.
/// </summary>
internal static class ServeStateJson
{
    public static string Build(ServeStateView state)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("generation", state.Generation);
            writer.WriteString("phase", PhaseName(state.Phase));

            if (state.SnapshotGeneration is { } snapGen)
                writer.WriteNumber("snapshotGeneration", snapGen);
            else
                writer.WriteNull("snapshotGeneration");

            if (state.SnapshotETag is { } etag)
                writer.WriteString("snapshotEtag", etag);
            else
                writer.WriteNull("snapshotEtag");

            if (state.LastSuccessUtc is { } success)
                writer.WriteString("lastSuccessUtc", success.UtcDateTime.ToString("o"));
            else
                writer.WriteNull("lastSuccessUtc");

            if (state.LastError is { } error)
                writer.WriteString("lastError", error);
            else
                writer.WriteNull("lastError");

            if (state.LastMetrics is { } metrics)
            {
                writer.WriteStartObject("metrics");
                writer.WriteNumber("analysisMillis", metrics.AnalysisMillis);
                writer.WriteNumber("jsonSizeBytes", metrics.JsonSizeBytes);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("metrics");
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    static string PhaseName(ServePhase phase) => phase switch
    {
        ServePhase.Analyzing => "analyzing",
        ServePhase.Ready => "ready",
        ServePhase.Failed => "failed",
        _ => "ready",
    };
}
