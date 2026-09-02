using Microsoft.Extensions.DependencyInjection;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Pauses the flow until the triggering user posts another message on the same
/// platform/channel. Config: "TimeoutSeconds" (default 30). The reply is stored
/// in the "response" variable; a timeout (or run cancellation) produces a failed
/// node result so the runtime's error branch can take over.
/// </summary>
public class WaitResponseNode : INode
{
    /// <summary>Default timeout when "TimeoutSeconds" is not configured.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>Variable name the received message is stored under.</summary>
    public const string ResponseVariable = "response";

    public string Type => "wait_for_response";
    public string Category => "Messaging";
    public string Title => "Wait for Response";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var registry = ctx.Services.GetService<IResponseWaiterRegistry>();
        if (registry is null)
            return NodeResult.Fail("Response registry unavailable");

        var platform = ctx.Platform.Trim();
        var username = ctx.Username.Trim();
        if (platform.Length == 0)
            return NodeResult.Fail("No triggering platform to wait for a response on");

        if (username.Length == 0)
            return NodeResult.Fail("No triggering user to wait for a response from");

        var channel = ctx.Channel ?? string.Empty;

        var timeoutRaw = ctx.Substitute(NodeConfigLookup.GetOrDefault(
            node, DefaultTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "TimeoutSeconds"));
        if (!int.TryParse(timeoutRaw, out var timeoutSeconds) || timeoutSeconds < 0)
            return NodeResult.Fail($"Cannot parse 'TimeoutSeconds' value '{timeoutRaw}' as a non-negative integer");

        string? response;
        try
        {
            response = await registry.WaitForResponseAsync(
                platform, username, channel, TimeSpan.FromSeconds(timeoutSeconds), ctx.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return NodeResult.Fail("Wait for response was cancelled");
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Wait for response failed: {ex.Message}");
        }

        if (ctx.CancellationToken.IsCancellationRequested)
            return NodeResult.Fail("Wait for response was cancelled");

        if (response is null)
            return NodeResult.Fail($"No response within {timeoutSeconds}s");

        ctx.SetVariable(ResponseVariable, response);
        return NodeResult.Success(response);
    }
}
