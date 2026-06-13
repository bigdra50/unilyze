using System.Net;
using System.Security.Cryptography;

namespace Unilyze.Serve;

/// <summary>
/// Security boundary for the serve session. serve opens new attack surface (it serves
/// source bodies and untrusted-repo data over an HTTP origin), so every API request must
/// prove three things: it carries the per-start bearer token (never placed in the URL,
/// only embedded in the no-store <c>GET /</c> HTML), it targets the exact loopback Host,
/// and — when an Origin is present — it comes from the same origin. No CORS, no cookies.
/// The Host/Origin checks defend against DNS-rebinding from a malicious page.
/// </summary>
internal sealed class ServeAuth
{
    public ServeAuth(int port)
    {
        Token = GenerateToken();
        ExpectedHost = $"127.0.0.1:{port}";
        ExpectedOrigin = $"http://127.0.0.1:{port}";
    }

    public string Token { get; }

    public string ExpectedHost { get; }

    public string ExpectedOrigin { get; }

    static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// True when the Host header is exactly the loopback host:port. Rejecting anything
    /// else (including a rebound DNS name resolving to 127.0.0.1) is the DNS-rebinding
    /// defense; we never trust an arbitrary Host that happens to reach this listener.
    /// </summary>
    public bool IsHostAllowed(HttpListenerRequest request) =>
        string.Equals(request.Headers["Host"], ExpectedHost, StringComparison.Ordinal);

    /// <summary>
    /// True when there is no Origin (same-origin navigations omit it) or the Origin is
    /// exactly our loopback origin. A cross-origin page is rejected.
    /// </summary>
    public bool IsOriginAllowed(HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        return origin is null || string.Equals(origin, ExpectedOrigin, StringComparison.Ordinal);
    }

    public bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        if (header is null || !header.StartsWith("Bearer ", StringComparison.Ordinal))
            return false;

        var presented = header["Bearer ".Length..];
        return FixedTimeEquals(presented, Token);
    }

    static bool FixedTimeEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
