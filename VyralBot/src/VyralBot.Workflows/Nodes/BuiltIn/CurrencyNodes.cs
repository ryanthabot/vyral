using Microsoft.Extensions.DependencyInjection;
using VyralBot.Core.Services.Currency;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Adds currency to a user's balance via the Core ICurrencyService.
/// Config: "amount" (integer, variable-substituted), optional "username"
/// (defaults to the triggering user) and "platform" (defaults to the triggering
/// platform), optional "reason".
/// </summary>
public class AddCurrencyNode : INode
{
    public string Type => "add_currency";
    public string Category => "Economy";
    public string Title => "Add Currency";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        CurrencyNodeHelper.MutateBalanceAsync(node, ctx, add: true);
}

/// <summary>Removes currency from a user's balance (see <see cref="AddCurrencyNode"/> for config).</summary>
public class RemoveCurrencyNode : INode
{
    public string Type => "remove_currency";
    public string Category => "Economy";
    public string Title => "Remove Currency";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        CurrencyNodeHelper.MutateBalanceAsync(node, ctx, add: false);
}

internal static class CurrencyNodeHelper
{
    public static async Task<NodeResult> MutateBalanceAsync(WorkflowNode node, ExecutionContext ctx, bool add)
    {
        var currency = ctx.Services.GetService<ICurrencyService>();
        if (currency is null)
            return NodeResult.Fail("ICurrencyService is not available");

        var username = ctx.Substitute(NodeConfig.Get(node, "username")).Trim();
        if (string.IsNullOrWhiteSpace(username))
            username = ctx.Username.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return NodeResult.Fail("No username (node config or triggering user) to adjust currency for");

        var platform = ctx.Substitute(NodeConfig.Get(node, "platform")).Trim();
        if (string.IsNullOrWhiteSpace(platform))
            platform = ctx.Platform.Trim();
        if (string.IsNullOrWhiteSpace(platform))
            return NodeResult.Fail("No platform (node config or trigger context) to adjust currency on");

        var amountRaw = ctx.Substitute(NodeConfig.Get(node, "amount"));
        if (!int.TryParse(amountRaw, out var amount) || amount < 0)
            return NodeResult.Fail($"Cannot parse 'amount' value '{amountRaw}' as a non-negative integer");

        var reason = ctx.Substitute(NodeConfig.Get(node, "reason"));
        if (string.IsNullOrWhiteSpace(reason))
            reason = add ? "Workflow: add_currency node" : "Workflow: remove_currency node";

        var ok = add
            ? await currency.AddCurrencyAsync(username, platform, amount, reason, "Workflow").ConfigureAwait(false)
            : await currency.RemoveCurrencyAsync(username, platform, amount, reason, "Workflow").ConfigureAwait(false);

        return ok
            ? NodeResult.Success(amount.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : NodeResult.Fail($"{(add ? "AddCurrency" : "RemoveCurrency")}Async returned false for '{username}' on '{platform}'");
    }
}
