using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using VyralBot.Data;

namespace VyralBot.Api.Middleware;

public class KickWebhookValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<KickWebhookValidationMiddleware> _logger;
    private readonly IDataService? _injectedDataService;
    private static string? _cachedPublicKey;
    private static DateTime _publicKeyExpiry = DateTime.MinValue;
    private static readonly TimeSpan PublicKeyCacheDuration = TimeSpan.FromHours(1);

    // Opt-in switch for the test-webhook signature bypass (KI-19). Default false:
    // every payload, test-shaped or not, must carry a valid RSA-SHA256 signature.
    private const string AllowTestWebhooksConfigKey = "Kick.AllowTestWebhooks";
    private static readonly TimeSpan TestBypassFlagCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly SemaphoreSlim TestBypassFlagLock = new(1, 1);
    private static bool _allowTestWebhooks;
    private static DateTime _allowTestWebhooksExpiryUtc = DateTime.MinValue;

    // Replay protection: Kick message IDs are unique per delivery, but a valid
    // signature stays valid on replay, so recently processed IDs are tracked and
    // duplicates are acknowledged without reprocessing. Combined with the
    // timestamp freshness window below this bounds replay to the window itself.
    private static readonly ConcurrentDictionary<string, DateTime> ProcessedMessageIds = new();
    private const int MaxTrackedMessageIds = 5000;
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TimestampMaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TimestampMaxFuture = TimeSpan.FromMinutes(2);
    private const int MaxWebhookBodyBytes = 262_144; // 256 KB — webhooks are small JSON documents

    // IDataService is only registered in DI when the Api project hosts itself
    // (Program.cs). Inside the WPF host the middleware runs in a Kestrel mini-host
    // whose child container does not include IDataService, so the WPF host's static
    // App.ServiceProvider is located lazily via reflection instead.
    private static IServiceProvider? _wpfHostProvider;
    private static bool _wpfHostProviderLookupAttempted;
    private static IDataService? _resolvedDataService;

    public KickWebhookValidationMiddleware(RequestDelegate next, ILogger<KickWebhookValidationMiddleware> logger, IDataService? dataService = null)
    {
        _next = next;
        _logger = logger;
        _injectedDataService = dataService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only validate Kick webhook requests
        if (!context.Request.Path.StartsWithSegments("/webhooks/kick"))
        {
            await _next(context);
            return;
        }

        // Skip validation for health check
        if (context.Request.Method == "GET")
        {
            await _next(context);
            return;
        }

        try
        {
            // Extract required headers
            var signature = context.Request.Headers["Kick-Event-Signature"].FirstOrDefault();
            var messageId = context.Request.Headers["Kick-Event-Message-Id"].FirstOrDefault();
            var timestamp = context.Request.Headers["Kick-Event-Message-Timestamp"].FirstOrDefault();

            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(timestamp))
            {
                _logger.LogWarning("Missing required webhook validation headers. Signature: {HasSignature}, MessageId: {HasMessageId}, Timestamp: {HasTimestamp}", 
                    !string.IsNullOrEmpty(signature), !string.IsNullOrEmpty(messageId), !string.IsNullOrEmpty(timestamp));
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Missing required headers");
                return;
            }

            _logger.LogInformation("Received Kick webhook - MessageId: {MessageId}, Timestamp: {Timestamp}, BodyLength: {BodyLength}", 
                messageId, timestamp, context.Request.ContentLength ?? 0);

            // Freshness: the timestamp is covered by the signature, so rejecting
            // stale or far-future timestamps bounds how long a captured delivery
            // can be replayed, even before the message-ID dedup below.
            if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sentAt))
            {
                _logger.LogWarning("Kick webhook has an unparseable timestamp header. MessageId: {MessageId}", messageId);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid timestamp");
                return;
            }

            var timestampAge = DateTimeOffset.UtcNow - sentAt;
            if (timestampAge > TimestampMaxAge || timestampAge < -TimestampMaxFuture)
            {
                _logger.LogWarning("Kick webhook timestamp outside the allowed window (age {Age}). MessageId: {MessageId}", timestampAge, messageId);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Stale timestamp");
                return;
            }

            // Payload size cap: webhooks are small JSON documents. Rejecting early
            // (and lowering the Kestrel body limit for this request, which also
            // covers chunked bodies) prevents memory-pressure abuse via the tunnel.
            if (context.Request.ContentLength is > MaxWebhookBodyBytes)
            {
                _logger.LogWarning("Kick webhook body too large ({Length} bytes). MessageId: {MessageId}", context.Request.ContentLength, messageId);
                context.Response.StatusCode = 413; // Payload Too Large
                await context.Response.WriteAsync("Payload too large");
                return;
            }

            if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { } bodySizeFeature)
            {
                bodySizeFeature.MaxRequestBodySize = MaxWebhookBodyBytes;
            }

            // Read request body
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            // Check if this is a test webhook. Test payloads are only exempt from
            // signature validation when the opt-in "Kick.AllowTestWebhooks" config
            // flag is enabled; by default they are validated like any other webhook
            // so the public tunnel URL cannot be used to inject forged events (KI-19).
            var isTestWebhook = messageId?.StartsWith("test-") == true || body.Contains("\"test\":true");
            var skipSignatureValidation = false;

            if (isTestWebhook)
            {
                skipSignatureValidation = await IsTestWebhookBypassAllowedAsync();

                if (skipSignatureValidation)
                {
                    _logger.LogWarning("⚠️ TEST WEBHOOK DETECTED - Skipping signature validation (config '{ConfigKey}' is enabled) for MessageId: {MessageId}",
                        AllowTestWebhooksConfigKey, messageId);
                }
                else
                {
                    _logger.LogWarning("Test-shaped webhook payload detected (MessageId: {MessageId}) but the test-webhook bypass is disabled - enforcing signature validation", messageId);
                }
            }

            if (!skipSignatureValidation)
            {
                // Validate signature for real webhooks
                var isValid = await ValidateSignature(signature!, messageId!, timestamp!, body);

                if (!isValid)
                {
                    _logger.LogError("❌ WEBHOOK SIGNATURE VALIDATION FAILED - MessageId: {MessageId}, Timestamp: {Timestamp}, Signature (first 50 chars): {Signature}, Body length: {BodyLength}",
                        messageId, timestamp, signature.Length > 50 ? signature.Substring(0, 50) : signature, body.Length);
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Invalid signature");
                    return;
                }

                _logger.LogDebug("Webhook signature validated successfully for message ID: {MessageId}", messageId);
            }

            // Replay protection: acknowledge duplicate deliveries (Kick retries on
            // non-2xx) but do not reprocess them, so captured valid payloads cannot
            // re-trigger events. Runs after validation so only authentic (or
            // explicitly test-bypassed) deliveries occupy the cache.
            var receivedAtUtc = DateTime.UtcNow;
            if (!ProcessedMessageIds.TryAdd(messageId!, receivedAtUtc))
            {
                _logger.LogInformation("Duplicate Kick webhook delivery acknowledged without processing (MessageId: {MessageId})", messageId);
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("Duplicate ignored");
                return;
            }
            PruneProcessedMessageIds(receivedAtUtc);

            // Continue to next middleware
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Internal server error");
        }
    }

    /// <summary>
    /// Bounds the replay-protection cache: once over capacity, expired entries
    /// (older than the replay window) are dropped. Best-effort and cheap — it
    /// runs only when the cap is exceeded, not per request.
    /// </summary>
    private static void PruneProcessedMessageIds(DateTime nowUtc)
    {
        if (ProcessedMessageIds.Count <= MaxTrackedMessageIds)
        {
            return;
        }

        foreach (var (id, seenAt) in ProcessedMessageIds)
        {
            if (nowUtc - seenAt > ReplayWindow)
            {
                ProcessedMessageIds.TryRemove(id, out _);
            }
        }
    }

    /// <summary>
    /// Reads the opt-in "Kick.AllowTestWebhooks" config flag, cached briefly so a
    /// webhook burst does not hammer the database. Any failure resolves to false,
    /// meaning signature validation is always enforced.
    /// </summary>
    private async Task<bool> IsTestWebhookBypassAllowedAsync()
    {
        if (DateTime.UtcNow < _allowTestWebhooksExpiryUtc)
        {
            return _allowTestWebhooks;
        }

        await TestBypassFlagLock.WaitAsync();
        try
        {
            // Double-check after acquiring the lock - another request may have refreshed it
            if (DateTime.UtcNow < _allowTestWebhooksExpiryUtc)
            {
                return _allowTestWebhooks;
            }

            var allowed = false;
            try
            {
                var dataService = ResolveDataService();
                if (dataService != null)
                {
                    var value = await dataService.GetConfigValueAsync(AllowTestWebhooksConfigKey);
                    var normalized = value?.Trim();
                    allowed = string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(normalized, "1", StringComparison.Ordinal);
                }
                else
                {
                    _logger.LogDebug("Could not resolve IDataService to read '{ConfigKey}' - keeping signature validation enforced",
                        AllowTestWebhooksConfigKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read '{ConfigKey}' - keeping signature validation enforced", AllowTestWebhooksConfigKey);
            }

            _allowTestWebhooks = allowed;
            _allowTestWebhooksExpiryUtc = DateTime.UtcNow.Add(TestBypassFlagCacheDuration);
            return allowed;
        }
        finally
        {
            TestBypassFlagLock.Release();
        }
    }

    /// <summary>
    /// Resolves the IDataService to use for config lookups. Prefers constructor
    /// injection (available when the Api project self-hosts via Program.cs); falls
    /// back to the WPF host's static service provider for the embedded Kestrel host.
    /// </summary>
    private IDataService? ResolveDataService()
    {
        if (_injectedDataService != null)
        {
            return _injectedDataService;
        }

        if (_resolvedDataService != null)
        {
            return _resolvedDataService;
        }

        var provider = TryGetWpfHostServiceProvider();
        if (provider != null)
        {
            try
            {
                _resolvedDataService = provider.GetService(typeof(IDataService)) as IDataService;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve IDataService from the WPF host provider");
            }
        }

        return _resolvedDataService;
    }

    /// <summary>
    /// Locates the WPF application's static App.ServiceProvider via reflection.
    /// VyralBot.Api does not reference VyralBot.UI at compile time, but both run in
    /// the same process when the web server is embedded in the desktop app. Returns
    /// null when unavailable (e.g. self-hosted), which keeps the bypass disabled.
    /// </summary>
    private static IServiceProvider? TryGetWpfHostServiceProvider()
    {
        if (_wpfHostProviderLookupAttempted)
        {
            return _wpfHostProvider;
        }

        _wpfHostProviderLookupAttempted = true;
        try
        {
            var appType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VyralBot.UI.App", throwOnError: false))
                .FirstOrDefault(t => t != null);

            var property = appType?.GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Static);
            if (property != null && typeof(IServiceProvider).IsAssignableFrom(property.PropertyType))
            {
                _wpfHostProvider = property.GetValue(null) as IServiceProvider;
            }
        }
        catch
        {
            // Leave null - the test-webhook bypass simply stays disabled
        }

        return _wpfHostProvider;
    }

    private async Task<bool> ValidateSignature(string signature, string messageId, string timestamp, string body)
    {
        try
        {
            // Get Kick public key (cached)
            var publicKey = await GetKickPublicKeyAsync();
            if (string.IsNullOrEmpty(publicKey))
            {
                _logger.LogError("Failed to retrieve Kick public key");
                return false;
            }

            // Construct the signed message: {messageId}.{timestamp}.{body}
            var signedMessage = $"{messageId}.{timestamp}.{body}";
            var signedMessageBytes = Encoding.UTF8.GetBytes(signedMessage);

            // Decode the signature from base64
            var signatureBytes = Convert.FromBase64String(signature);

            // Import RSA public key
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);

            // Verify signature using RSA SHA256
            var isValid = rsa.VerifyData(
                signedMessageBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating signature");
            return false;
        }
    }

    private async Task<string?> GetKickPublicKeyAsync()
    {
        // Return cached key if still valid
        if (!string.IsNullOrEmpty(_cachedPublicKey) && DateTime.UtcNow < _publicKeyExpiry)
        {
            _logger.LogDebug("Using cached public key (expires at {Expiry})", _publicKeyExpiry);
            return _cachedPublicKey;
        }

        _logger.LogInformation("🔄 Fetching fresh Kick public key from API (cache expired or empty)");

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await httpClient.GetAsync("https://api.kick.com/public/v1/public-key");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to retrieve Kick public key: {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("✅ Kick public key API response received, length: {Length}", content.Length);
            
            // Parse JSON response to extract public_key
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
            if (!jsonDoc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("public_key", out var publicKeyProp))
            {
                _logger.LogError("Invalid response format from public key endpoint");
                return null;
            }

            var publicKeyRaw = publicKeyProp.GetString();
            if (string.IsNullOrEmpty(publicKeyRaw))
            {
                _logger.LogError("Empty public key in response");
                return null;
            }

            // Replace escaped newlines with actual newlines
            var publicKey = publicKeyRaw.Replace("\\n", "\n");
            
            // Cache the public key
            _cachedPublicKey = publicKey;
            _publicKeyExpiry = DateTime.UtcNow.Add(PublicKeyCacheDuration);

            _logger.LogInformation("Retrieved and cached Kick public key (expires: {Expiry})", _publicKeyExpiry);

            return _cachedPublicKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Kick public key");
            return null;
        }
    }
}
