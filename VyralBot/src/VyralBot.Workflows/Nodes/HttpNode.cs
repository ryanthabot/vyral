using System.Text;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Calls an external HTTP API with a static shared client (15s timeout).
/// Config: "Method" (GET/POST/PUT/DELETE, default GET), "Url" ({variable}-
/// substituted), "Headers" (one "Key: Value" per line), "Body" (optional,
/// {variable}-substituted), "SaveAs" (variable name for the response body,
/// default "http_response"; the status code is also stored as "http_status").
/// The node succeeds whenever the request completed — even on non-2xx statuses
/// (branch on {http_status} afterwards); it only fails on transport errors,
/// timeouts or invalid configuration.
/// </summary>
public class HttpRequestNode : INode
{
    /// <summary>Hard client timeout for a single request.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Default variable name for the response body.</summary>
    public const string DefaultSaveAs = "http_response";

    /// <summary>Variable name the HTTP status code is stored under.</summary>
    public const string StatusCodeVariable = "http_status";

    private static readonly HttpClient HttpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.Timeout = RequestTimeout;
        return client;
    }

    public string Type => "http_request";
    public string Category => "Network";
    public string Title => "HTTP Request";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var methodRaw = NodeConfigLookup.Get(node, "Method").Trim();
        if (methodRaw.Length == 0)
            methodRaw = "GET";

        HttpMethod? method = methodRaw.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            _ => null
        };

        if (method is null)
            return NodeResult.Fail($"Unsupported method '{methodRaw}' (use GET, POST, PUT or DELETE)");

        var url = ctx.Substitute(NodeConfigLookup.Get(node, "Url")).Trim();
        if (url.Length == 0)
            return NodeResult.Fail("No URL configured");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return NodeResult.Fail($"'{url}' is not a valid absolute http(s) URL");

        using var request = new HttpRequestMessage(method, uri);

        // Attach the body first so "Key: Value" lines may also target content headers.
        var body = ctx.Substitute(NodeConfigLookup.Get(node, "Body"));
        if (body.Length > 0)
        {
            // JSON-shaped bodies get the JSON content type; everything else plain text.
            var trimmed = body.TrimStart();
            var isJson = trimmed.StartsWith('{') || trimmed.StartsWith('[');
            request.Content = new StringContent(
                body,
                Encoding.UTF8,
                isJson ? "application/json" : "text/plain");
        }

        foreach (var rawLine in NodeConfigLookup.Get(node, "Headers").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                return NodeResult.Fail($"Invalid header line '{line}' (expected 'Key: Value')");

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (name.Length == 0)
                return NodeResult.Fail($"Invalid header line '{line}' (empty key)");

            if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content?.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            using var response = await HttpClient.SendAsync(request, ctx.CancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ctx.CancellationToken).ConfigureAwait(false);
            var statusCode = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);

            var saveAs = NodeConfigLookup.Get(node, "SaveAs").Trim();
            if (saveAs.Length == 0)
                saveAs = DefaultSaveAs;

            ctx.SetVariable(saveAs, responseBody);
            ctx.SetVariable(StatusCodeVariable, statusCode);

            // Success = the request completed; non-2xx statuses are the caller's problem ({http_status}).
            return NodeResult.Success(statusCode);
        }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
        {
            throw; // run-level cancellation — let the runtime record it
        }
        catch (Exception ex)
        {
            // Transport errors and the 15s client timeout (TaskCanceledException) land here.
            return NodeResult.Fail($"HTTP request to '{url}' failed: {ex.Message}");
        }
    }
}
