using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Unilyze.Tests.Serve;

public sealed class ServeE2eTests : IDisposable
{
    readonly List<string> _temps = new();

    string NewProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "unilyze-serve-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Alpha.cs"),
            "namespace S; public class Alpha { public int X() => 1; }\n");
        _temps.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _temps)
        {
            try { Directory.Delete(d, true); } catch { /* best effort */ }
        }
    }

    sealed record StateView(long Generation, string Phase, long? SnapshotGeneration, string? SnapshotEtag, string? LastError);

    static async Task<string> TokenAsync(ServeProcess serve)
    {
        var html = await serve.Client.GetStringAsync(serve.BaseUrl);
        var m = Regex.Match(html, "name=\"unilyze-token\" content=\"([^\"]+)\"");
        Assert.True(m.Success, "token meta tag not found in served HTML");
        return m.Groups[1].Value;
    }

    static HttpRequestMessage Authed(string url, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return req;
    }

    static async Task<StateView> StateAsync(ServeProcess serve, string token, long after)
    {
        using var resp = await serve.Client.SendAsync(Authed($"{serve.BaseUrl}api/state?after={after}", token));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        long? snapGen = root.GetProperty("snapshotGeneration").ValueKind == JsonValueKind.Null
            ? null : root.GetProperty("snapshotGeneration").GetInt64();
        string? etag = root.GetProperty("snapshotEtag").ValueKind == JsonValueKind.Null
            ? null : root.GetProperty("snapshotEtag").GetString();
        string? err = root.GetProperty("lastError").ValueKind == JsonValueKind.Null
            ? null : root.GetProperty("lastError").GetString();
        return new StateView(
            root.GetProperty("generation").GetInt64(),
            root.GetProperty("phase").GetString() ?? "",
            snapGen, etag, err);
    }

    static async Task<StateView> WaitForPhaseAsync(ServeProcess serve, string token, string phase, int timeoutMs = 40000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        long after = -1;
        StateView last = await StateAsync(serve, token, after);
        if (last.Phase == phase) return last;
        while (DateTime.UtcNow < deadline)
        {
            last = await StateAsync(serve, token, last.Generation);
            if (last.Phase == phase) return last;
        }
        throw new TimeoutException($"phase '{phase}' not reached; last={last.Phase} gen={last.Generation}");
    }

    static async Task<StateView> WaitForReadyAsync(ServeProcess serve, string token) =>
        await WaitForPhaseAsync(serve, token, "ready");

    [Fact]
    public async Task Serve_PrintsUrlOnStderr_AndServesIndexWithToken()
    {
        using var serve = ServeProcess.Start(NewProject());
        Assert.Contains("listening on", serve.StdErr());

        using var resp = await serve.Client.GetAsync(serve.BaseUrl);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.Contains("Content-Security-Policy"));
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("unilyze-token", html);
        Assert.Contains("/static/main.js", html);
    }

    [Fact]
    public async Task Api_WithoutBearer_Returns401()
    {
        using var serve = ServeProcess.Start(NewProject());
        using var state = await serve.Client.GetAsync($"{serve.BaseUrl}api/state");
        using var snap = await serve.Client.GetAsync($"{serve.BaseUrl}api/snapshot");
        Assert.Equal(HttpStatusCode.Unauthorized, state.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, snap.StatusCode);
    }

    [Fact]
    public async Task Api_WithBearer_ReturnsState()
    {
        using var serve = ServeProcess.Start(NewProject());
        var token = await TokenAsync(serve);
        var state = await StateAsync(serve, token, -1);
        Assert.True(state.Generation >= 0);
        Assert.Contains(state.Phase, new[] { "analyzing", "ready", "failed" });
    }

    [Fact]
    public async Task Snapshot_HasETag_AndReturns304OnIfNoneMatch()
    {
        using var serve = ServeProcess.Start(NewProject());
        var token = await TokenAsync(serve);
        await WaitForReadyAsync(serve, token);

        using var first = await serve.Client.SendAsync(Authed($"{serve.BaseUrl}api/snapshot", token));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag?.Tag ?? first.Headers.GetValues("ETag").First();
        Assert.False(string.IsNullOrEmpty(etag));

        var conditional = Authed($"{serve.BaseUrl}api/snapshot", token);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var second = await serve.Client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task SourceChange_BumpsGeneration()
    {
        var dir = NewProject();
        using var serve = ServeProcess.Start(dir);
        var token = await TokenAsync(serve);
        var ready = await WaitForReadyAsync(serve, token);

        File.WriteAllText(Path.Combine(dir, "Alpha.cs"),
            "namespace S; public class Alpha { public int X() => 1; public int Y() => 2; }\n");

        var deadline = DateTime.UtcNow.AddSeconds(40);
        long after = ready.Generation;
        StateView st = ready;
        while (DateTime.UtcNow < deadline)
        {
            st = await StateAsync(serve, token, after);
            after = st.Generation;
            if (st.Phase == "ready" && st.Generation > ready.Generation)
                break;
        }
        Assert.True(st.Generation > ready.Generation, $"generation did not advance (was {ready.Generation}, now {st.Generation})");
    }

    [Fact]
    public async Task AnalysisFailure_KeepsStaleSnapshot_ThenRecovers()
    {
        var dir = NewProject();
        using var serve = ServeProcess.Start(dir);
        var token = await TokenAsync(serve);
        var ready = await WaitForReadyAsync(serve, token);
        var goodEtag = ready.SnapshotEtag;
        Assert.NotNull(goodEtag);

        using (MakeAnalysisFail(dir))
        {
            var failed = await WaitForPhaseAsync(serve, token, "failed");
            Assert.NotNull(failed.LastError);

            // The previous good snapshot is retained (not blanked) and still served.
            using var snap = await serve.Client.SendAsync(Authed($"{serve.BaseUrl}api/snapshot", token));
            Assert.Equal(HttpStatusCode.OK, snap.StatusCode);
            var staleEtag = snap.Headers.ETag?.Tag ?? snap.Headers.GetValues("ETag").First();
            Assert.Equal(goodEtag, staleEtag);
        }

        // Removing the failing input recovers to a healthy snapshot.
        var recovered = await WaitForPhaseAsync(serve, token, "ready");
        Assert.Equal("ready", recovered.Phase);
    }

    [Fact]
    public async Task SourceBoundary_NoAbsolutePaths_AllowlistEnforced()
    {
        var dir = NewProject();
        using var serve = ServeProcess.Start(dir);
        var token = await TokenAsync(serve);
        await WaitForReadyAsync(serve, token);

        using var snapResp = await serve.Client.SendAsync(Authed($"{serve.BaseUrl}api/snapshot", token));
        var snapshotJson = await snapResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(dir, snapshotJson);

        using var doc = JsonDocument.Parse(snapshotJson);
        var fileId = doc.RootElement.GetProperty("types").EnumerateArray()
            .Select(t => t.TryGetProperty("filePath", out var fp) ? fp.GetString() : null)
            .First(v => !string.IsNullOrEmpty(v))!;

        using var src = await serve.Client.SendAsync(Authed($"{serve.BaseUrl}api/source?fileId={fileId}", token));
        Assert.Equal(HttpStatusCode.OK, src.StatusCode);
        Assert.Contains("Alpha", await src.Content.ReadAsStringAsync());

        using var unknown = await serve.Client.SendAsync(Authed($"{serve.BaseUrl}api/source?fileId=f99999", token));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var noAuth = await serve.Client.GetAsync($"{serve.BaseUrl}api/source?fileId={fileId}");
        Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);
    }

    [Fact]
    public async Task NonLoopbackHost_IsRejected()
    {
        using var serve = ServeProcess.Start(NewProject());
        var req = new HttpRequestMessage(HttpMethod.Get, serve.BaseUrl);
        req.Headers.Host = "evil.example.com";
        using var resp = await serve.Client.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    // A cross-platform way to make the next analysis throw (not a tolerated C# parse error):
    // a broken symlink on Unix, an exclusive file lock on Windows. Both also fire a .cs
    // change event so re-analysis runs.
    static IDisposable MakeAnalysisFail(string dir)
    {
        if (OperatingSystem.IsWindows())
        {
            var f = Path.Combine(dir, "Locked.cs");
            File.WriteAllText(f, "namespace S; public class Locked {}\n");
            var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);
            return new Cleanup(() => { fs.Dispose(); try { File.Delete(f); } catch { /* best effort */ } });
        }

        var link = Path.Combine(dir, "Broken.cs");
        File.CreateSymbolicLink(link, Path.Combine(Path.GetTempPath(), "unilyze-missing-" + Guid.NewGuid().ToString("N") + ".cs"));
        return new Cleanup(() => { try { File.Delete(link); } catch { /* best effort */ } });
    }

    sealed class Cleanup : IDisposable
    {
        readonly Action _dispose;
        public Cleanup(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}
