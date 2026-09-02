using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace VyralBot.Api.Middleware;

public class KickWebhookValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<KickWebhookValidationMiddleware> _logger;
    private static string? _cachedPublicKey;
    private static DateTime _publicKeyExpiry = DateTime.MinValue;
    private static readonly TimeSpan PublicKeyCacheDuration = TimeSpan.FromHours(1);

    public KickWebhookValidationMiddleware(RequestDelegate next, ILogger<KickWebhookValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

            // Read request body
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            // Check if this is a test webhook (skip signature validation for testing)
            var isTestWebhook = messageId?.StartsWith("test-") == true || body.Contains("\"test\":true");
            
            if (isTestWebhook)
            {
                _logger.LogWarning("⚠️ TEST WEBHOOK DETECTED - Skipping signature validation for MessageId: {MessageId}", messageId);
            }
            else
            {
                // Validate signature for real webhooks
                var isValid = await ValidateSignature(signature!, messageId!, timestamp!, body);

                if (!isValid)
                {
                    _logger.LogError("❌ WEBHOOK SIGNATURE VALIDATION FAILED - MessageId: {MessageId}, Timestamp: {Timestamp}, Signature (first 50 chars): {Signature}, Body length: {BodyLength}", 
                        messageId, timestamp, signature.Length > 50 ? signature.Substring(0, 50) : signature, body.Length);
                    _logger.LogError("Body preview (first 200 chars): {BodyPreview}", body.Length > 200 ? body.Substring(0, 200) : body);
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Invalid signature");
                    return;
                }

                _logger.LogDebug("Webhook signature validated successfully for message ID: {MessageId}", messageId);
            }

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
            using var httpClient = new HttpClient();
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
