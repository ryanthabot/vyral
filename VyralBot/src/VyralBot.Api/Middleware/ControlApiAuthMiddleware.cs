using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VyralBot.Api.Services;

namespace VyralBot.Api.Middleware;

/// <summary>
/// Absolute auth gate for the control API: every request whose path starts with
/// <c>/api</c> must carry <c>Authorization: Bearer &lt;token&gt;</c> where the token
/// matches the existing webhook auth token (the ngrok auth token loaded by
/// <see cref="NgrokService"/> from the per-profile credentials JSON). Because the
/// check is a path-prefix middleware — not a controller attribute — any future
/// route added under <c>/api</c> is gated by construction, including when the
/// host is exposed publicly through an ngrok tunnel.
///
/// Fail-closed behaviors:
/// - No token configured → 503 for the whole <c>/api</c> branch (API disabled).
/// - Missing/malformed/bad Bearer token → 401 JSON, never a route match.
/// - Bodies over 16 KB → 413 (test-event payloads are a few hundred bytes).
/// </summary>
public class ControlApiAuthMiddleware
{
    private const string BearerPrefix = "Bearer ";
    private const int MaxApiBodyBytes = 16_384; // 16 KB — /api bodies are tiny JSON documents

    private readonly RequestDelegate _next;
    private readonly ILogger<ControlApiAuthMiddleware> _logger;
    private readonly NgrokService? _ngrokService;

    public ControlApiAuthMiddleware(RequestDelegate next, ILogger<ControlApiAuthMiddleware> logger, NgrokService? ngrokService = null)
    {
        _next = next;
        _logger = logger;
        _ngrokService = ngrokService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Size cap first — reject oversized bodies before any parsing, and lower
        // the Kestrel limit for this request so chunked bodies are bounded too.
        if (context.Request.ContentLength is > MaxApiBodyBytes)
        {
            _logger.LogWarning("Rejected oversized control API body ({Length} bytes) from {RemoteIp}",
                context.Request.ContentLength, context.Connection.RemoteIpAddress);
            await WriteJsonAsync(context, 413, new { error = "payload_too_large" });
            return;
        }

        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { } bodySizeFeature)
        {
            bodySizeFeature.MaxRequestBodySize = MaxApiBodyBytes;
        }

        // The webhook auth token doubles as the control API bearer token. It is
        // loaded from the per-profile credentials file and is never logged.
        var configuredToken = _ngrokService?.AuthToken;
        if (string.IsNullOrEmpty(configuredToken))
        {
            _logger.LogWarning("Control API request rejected: no webhook auth token is configured");
            await WriteJsonAsync(context, 503, new
            {
                error = "api_disabled",
                message = "No webhook auth token configured. Set the ngrok auth token in Web Server settings first."
            });
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.Length < BearerPrefix.Length ||
            !authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        var providedToken = authHeader[BearerPrefix.Length..].Trim();
        if (!TokensEqual(providedToken, configuredToken))
        {
            _logger.LogWarning("Control API request rejected: invalid bearer token from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            await WriteUnauthorizedAsync(context);
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Fixed-time comparison over SHA-256 digests so token length differences
    /// cannot be used as an oracle (same pattern as the VyralDeck device tokens).
    /// </summary>
    private static bool TokensEqual(string provided, string configured)
    {
        if (provided.Length == 0)
        {
            return false;
        }

        byte[] providedHash, configuredHash;
        using (var sha = SHA256.Create())
        {
            providedHash = sha.ComputeHash(Encoding.UTF8.GetBytes(provided));
            configuredHash = sha.ComputeHash(Encoding.UTF8.GetBytes(configured));
        }

        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await WriteJsonAsync(context, 401, new { error = "unauthorized" });
    }

    private static async Task WriteJsonAsync(HttpContext context, int statusCode, object payload)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload), context.RequestAborted);
    }
}
