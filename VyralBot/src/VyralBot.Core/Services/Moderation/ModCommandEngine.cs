using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Default <see cref="IModCommandEngine"/>. Gates messages to the configured
/// mod channel, resolves the caller's effective tier via
/// <see cref="IModTierResolver"/> (explicit assignment or the highest
/// Discord-role→tier mapping), enforces the <see cref="ModTiers"/> ladder,
/// executes the action and audits every executed action through
/// <see cref="IDataService.SaveModActionAsync"/>.
/// Every reply goes back into the mod channel through the bot resolved by
/// <see cref="BotResolver"/>.
///
/// Because VyralBot.Core cannot reference VyralBot.Integrations (the reference
/// direction is Integrations → Core), all Discord-specific access flows through
/// the Core-side seams (<see cref="ModChannelMessage"/>, <see cref="IModDiscordBot"/>,
/// <see cref="DiscordChatSubscriber"/>, <see cref="ModerationInvoker"/>) that the
/// App.xaml.cs composition wires to the real Discord types.
/// </summary>
public class ModCommandEngine : IModCommandEngine
{
    private const string CommandPrefix = "!mod";
    private const string DiscordPlatform = "Discord";

    /// <summary>Actions delegated to the underlying Discord command handler.</summary>
    private static readonly HashSet<string> DelegatedActions = new(StringComparer.Ordinal)
    {
        "kick", "ban", "mute", "unban", "timeout", "unmute", "purge", "slowmode"
    };

    private readonly IModRoleService _modRoles;
    private readonly IModTierResolver _tierResolver;
    private readonly IDataService _dataService;
    private readonly ICurrencyService _currency;
    private readonly GiveawayService _giveaways;
    private readonly PollService _polls;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ModCommandEngine> _logger;

    private Guid _subscriptionId;
    private bool _subscribed;

    public ModCommandEngine(
        IModRoleService modRoles,
        IModTierResolver tierResolver,
        IDataService dataService,
        ICurrencyService currency,
        GiveawayService giveaways,
        PollService polls,
        IEventBus eventBus,
        ILogger<ModCommandEngine>? logger = null)
    {
        _modRoles = modRoles ?? throw new ArgumentNullException(nameof(modRoles));
        _tierResolver = tierResolver ?? throw new ArgumentNullException(nameof(tierResolver));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        _giveaways = giveaways ?? throw new ArgumentNullException(nameof(giveaways));
        _polls = polls ?? throw new ArgumentNullException(nameof(polls));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? NullLogger<ModCommandEngine>.Instance;
    }

    /// <summary>
    /// Returns the configured mod-command channel snowflake id. Null (or a
    /// null/empty result) makes the engine inert. Set by the DI composition.
    /// </summary>
    public Func<string?>? ModChannelIdProvider { get; set; }

    /// <summary>
    /// Maps an event's BotId to that bot's Discord service (wrapped in
    /// <see cref="IModDiscordBot"/>). Returning null drops the command (nothing
    /// to reply with). Set by the DI composition.
    /// </summary>
    public Func<string, IModDiscordBot?>? BotResolver { get; set; }

    /// <summary>
    /// Subscribes a handler to DiscordChatMessageEvent (projected onto
    /// <see cref="ModChannelMessage"/>) and returns the subscription Guid that
    /// <see cref="StopAsync"/> unsubscribes. Set by the DI composition, e.g.:
    /// <code>
    /// engine.DiscordChatSubscriber = handler => eventBus.SubscribeAsync&lt;DiscordChatMessageEvent&gt;(
    ///     e => handler(new ModChannelMessage(e.ChannelId, e.SenderUserId, e.Username, e.Message, e.BotId)));
    /// </code>
    /// </summary>
    public Func<Func<ModChannelMessage, Task>, Task<Guid>>? DiscordChatSubscriber { get; set; }

    /// <summary>
    /// Forwards a raw moderation command line (e.g. "!kick @user") to the
    /// underlying Discord command handler for the bot with the given id, in the
    /// given channel, with callerIsModerator=true / callerIsOwner=false.
    /// Returns the handler's chat-ready response, or null when the command was
    /// not handled. Set by the DI composition.
    /// </summary>
    public Func<string, string, string?, Task<string?>>? ModerationInvoker { get; set; }

    /// <inheritdoc />
    public async Task StartAsync()
    {
        if (DiscordChatSubscriber == null)
        {
            _logger.LogWarning(
                "ModCommandEngine has no DiscordChatSubscriber wired — not listening for !mod commands");
            return;
        }

        if (_subscribed)
        {
            return;
        }

        _subscriptionId = await DiscordChatSubscriber(HandleMessageAsync).ConfigureAwait(false);
        _subscribed = true;
        _logger.LogInformation(
            "ModCommandEngine subscribed to Discord chat messages (subscription {SubscriptionId})", _subscriptionId);
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        if (_subscribed)
        {
            _eventBus.Unsubscribe(_subscriptionId);
            _subscribed = false;
            _logger.LogInformation(
                "ModCommandEngine unsubscribed from Discord chat messages (subscription {SubscriptionId})",
                _subscriptionId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles one projected Discord chat message. Public because the DI
    /// composition (and the tests) feed it directly; the engine's own
    /// subscription path calls it too.
    /// </summary>
    public async Task HandleMessageAsync(ModChannelMessage? message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.Message))
        {
            return;
        }

        var modChannelId = ModChannelIdProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(modChannelId))
        {
            // No mod channel configured — the engine is intentionally inert.
            return;
        }

        if (string.IsNullOrWhiteSpace(message.ChannelId) ||
            !string.Equals(message.ChannelId.Trim(), modChannelId!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // Only the mod channel carries !mod commands.
            return;
        }

        var text = message.Message.Trim();
        if (!TryParseCommand(text, out var action, out var args))
        {
            return;
        }

        var bot = BotResolver?.Invoke(message.BotId ?? "");
        if (bot == null)
        {
            _logger.LogWarning(
                "Ignoring !mod command from {Username}: no bot resolved for BotId {BotId}",
                message.Username, message.BotId);
            return;
        }

        try
        {
            await HandleCoreAsync(bot, modChannelId!.Trim(), message, action, args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling !mod command '{Message}' from {Username}", text, message.Username);
            await ReplyAsync(bot, modChannelId!, "❌ Something went wrong while running that mod command.").ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // parsing + dispatch
    // ------------------------------------------------------------------

    private static bool TryParseCommand(string text, out string? action, out string[] args)
    {
        action = null;
        args = Array.Empty<string>();

        if (!text.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // "!moderator ..." or "!modded ..." are NOT mod commands; "!mod" alone
        // or followed by whitespace is.
        if (text.Length > CommandPrefix.Length && !char.IsWhiteSpace(text[CommandPrefix.Length]))
        {
            return false;
        }

        var tokens = text.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return true; // bare "!mod" → usage reply
        }

        action = tokens[1].ToLowerInvariant();
        args = tokens.Skip(2).ToArray();
        return true;
    }

    private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

    private async Task HandleCoreAsync(
        IModDiscordBot bot, string modChannelId, ModChannelMessage message, string? action, string[] args)
    {
        if (action == null)
        {
            await ReplyAsync(bot, modChannelId, BuildUsage()).ConfigureAwait(false);
            return;
        }

        var tier = await _tierResolver.ResolveTierAsync(
            message.BotId ?? "",
            message.GuildId,
            message.SenderUserId ?? "",
            message.Username).ConfigureAwait(false);
        if (string.IsNullOrEmpty(tier))
        {
            await ReplyAsync(bot, modChannelId, "❌ You are not a moderator.").ConfigureAwait(false);
            return;
        }

        if (ModTiers.MinimumTier(action) == null)
        {
            await ReplyAsync(bot, modChannelId, $"❌ Unknown mod action '{action}'. {BuildUsage()}").ConfigureAwait(false);
            return;
        }

        if (!ModTiers.CanRun(tier, action))
        {
            var minimum = ModTiers.MinimumTier(action)!;
            await ReplyAsync(bot, modChannelId, $"❌ '{action}' requires {minimum} or higher.").ConfigureAwait(false);
            return;
        }

        var (reply, target, executed) = await ExecuteAsync(action, args, message).ConfigureAwait(false);

        if (executed)
        {
            await _dataService.SaveModActionAsync(new ModAction
            {
                ModUsername = message.Username ?? "",
                ModUserId = message.SenderUserId ?? "",
                TargetUsername = target ?? "",
                TargetUserId = "",
                Action = action,
                Details = reply
            }).ConfigureAwait(false);
        }

        if (reply != null)
        {
            await ReplyAsync(bot, modChannelId, reply).ConfigureAwait(false);
        }
    }

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> ExecuteAsync(
        string action, string[] args, ModChannelMessage message)
    {
        switch (action)
        {
            case "addcurrency":
            case "removecurrency":
                return await HandleCurrencyChangeAsync(action, args, message).ConfigureAwait(false);
            case "setbalance":
                return await HandleSetBalanceAsync(args, message).ConfigureAwait(false);
            case "startgiveaway":
                return await HandleStartGiveawayAsync(args, message).ConfigureAwait(false);
            case "cancelgiveaway":
                return await HandleCancelGiveawayAsync().ConfigureAwait(false);
            case "draw":
            case "drawwinners":
                return await HandleDrawAsync().ConfigureAwait(false);
            case "startpoll":
                return await HandleStartPollAsync(args, message).ConfigureAwait(false);
            case "endpoll":
                return await HandleEndPollAsync().ConfigureAwait(false);
            case "assignmod":
                return await HandleAssignModAsync(args, message).ConfigureAwait(false);
            default:
                return await HandleDelegatedModerationAsync(action, args, message).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // currency actions
    // ------------------------------------------------------------------

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleCurrencyChangeAsync(
        string action, string[] args, ModChannelMessage message)
    {
        if (args.Length < 2 ||
            !int.TryParse(args[1], out var amount) || amount <= 0)
        {
            return ($"❌ Usage: !mod {action} <username> <amount>", null, false);
        }

        var target = NormalizeTarget(args[0]);
        if (target.Length == 0)
        {
            return ($"❌ Usage: !mod {action} <username> <amount>", null, false);
        }

        var currencyName = await GetCurrencyNameAsync().ConfigureAwait(false);
        var reason = $"Mod {action} by {message.Username}";
        var isAdd = action == "addcurrency";

        var ok = isAdd
            ? await _currency.AddCurrencyAsync(target, DiscordPlatform, amount, reason, "Admin").ConfigureAwait(false)
            : await _currency.RemoveCurrencyAsync(target, DiscordPlatform, amount, reason, "Admin").ConfigureAwait(false);

        if (!ok)
        {
            var failure = isAdd
                ? $"❌ Failed to add {currencyName} to @{target}."
                : $"❌ Failed to remove {currencyName} from @{target} (insufficient balance?).";
            return (failure, target, true);
        }

        var balance = await _currency.GetBalanceAsync(target, DiscordPlatform).ConfigureAwait(false);
        var reply = isAdd
            ? $"✅ Added {amount:N0} {currencyName} to @{target}. New balance: {balance:N0}."
            : $"✅ Removed {amount:N0} {currencyName} from @{target}. New balance: {balance:N0}.";
        return (reply, target, true);
    }

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleSetBalanceAsync(
        string[] args, ModChannelMessage message)
    {
        if (args.Length < 2 ||
            !int.TryParse(args[1], out var amount) || amount < 0)
        {
            return ("❌ Usage: !mod setbalance <username> <amount>", null, false);
        }

        var target = NormalizeTarget(args[0]);
        if (target.Length == 0)
        {
            return ("❌ Usage: !mod setbalance <username> <amount>", null, false);
        }

        var currencyName = await GetCurrencyNameAsync().ConfigureAwait(false);
        var current = await _currency.GetBalanceAsync(target, DiscordPlatform).ConfigureAwait(false);
        var delta = amount - current;
        var reason = $"Mod setbalance by {message.Username}";

        var ok = true;
        if (delta > 0)
        {
            ok = await _currency.AddCurrencyAsync(target, DiscordPlatform, delta, reason, "Admin").ConfigureAwait(false);
        }
        else if (delta < 0)
        {
            ok = await _currency.RemoveCurrencyAsync(target, DiscordPlatform, -delta, reason, "Admin").ConfigureAwait(false);
        }

        if (!ok)
        {
            return ($"❌ Failed to set balance for @{target}.", target, true);
        }

        return ($"✅ Set @{target}'s balance to {amount:N0} {currencyName}.", target, true);
    }

    private async Task<string> GetCurrencyNameAsync()
    {
        try
        {
            var settings = await _currency.GetSettingsAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(settings?.CurrencyName) ? "points" : settings!.CurrencyName!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the currency name for mod replies — falling back to 'points'");
            return "points";
        }
    }

    // ------------------------------------------------------------------
    // giveaway actions
    // ------------------------------------------------------------------

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleStartGiveawayAsync(
        string[] args, ModChannelMessage message)
    {
        if (args.Length < 3 ||
            !int.TryParse(args[1], out var winnerCount) || winnerCount <= 0)
        {
            return ("❌ Usage: !mod startgiveaway <name> <winnerCount> <keyword>", null, false);
        }

        var name = args[0];
        var keyword = args[2];

        try
        {
            var giveaway = await _giveaways.CreateGiveawayAsync(
                name,
                $"Mod giveaway by {message.Username}",
                keyword,
                winnerCount).ConfigureAwait(false);

            var started = await _giveaways.StartGiveawayAsync(giveaway.Id).ConfigureAwait(false);
            if (!started)
            {
                return ("❌ Could not start the giveaway (another giveaway may already be active).", null, true);
            }

            return ($"✅ Giveaway '{name}' started — {winnerCount} winner(s). Enter with !{keyword}!", null, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the mod giveaway '{Name}'", name);
            return ("❌ Failed to create the giveaway.", null, false);
        }
    }

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleCancelGiveawayAsync()
    {
        var active = await _giveaways.GetActiveGiveawayAsync().ConfigureAwait(false);
        if (active == null)
        {
            return ("❌ No active giveaway to cancel.", null, false);
        }

        var ok = await _giveaways.CancelGiveawayAsync(active.Id).ConfigureAwait(false);
        return ok
            ? ($"✅ Cancelled giveaway '{active.Name}'.", null, true)
            : ("❌ Failed to cancel the giveaway.", null, true);
    }

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleDrawAsync()
    {
        var active = await _giveaways.GetActiveGiveawayAsync().ConfigureAwait(false);
        if (active == null)
        {
            return ("❌ No active giveaway to draw.", null, false);
        }

        var (ok, winners, drawMessage) = await _giveaways.DrawWinnersAsync(active.Id).ConfigureAwait(false);
        if (!ok)
        {
            return ($"❌ Draw failed: {drawMessage}", null, true);
        }

        return ($"✅ Draw complete for '{active.Name}' — winners: {string.Join(", ", winners)}", null, true);
    }

    // ------------------------------------------------------------------
    // poll actions
    // ------------------------------------------------------------------

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleStartPollAsync(
        string[] args, ModChannelMessage message)
    {
        // Format: !mod startpoll <question> | <option 1> | <option 2> [...]
        var joined = string.Join(" ", args);
        var parts = joined.Split('|');
        var question = parts[0].Trim();
        var options = parts.Skip(1).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();

        if (question.Length == 0 || options.Length < 2)
        {
            return ("❌ Usage: !mod startpoll <question> | <option 1> | <option 2> [...]", null, false);
        }

        try
        {
            var poll = await _polls.CreatePollAsync(question, options, message.Username ?? "mod").ConfigureAwait(false);
            var started = await _polls.StartPollAsync(poll.Id).ConfigureAwait(false);
            if (!started)
            {
                return ("❌ Could not start the poll.", null, true);
            }

            return ($"✅ Poll started: {question} ({options.Length} options)", null, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the mod poll '{Question}'", question);
            return ("❌ Failed to create the poll.", null, false);
        }
    }

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleEndPollAsync()
    {
        var active = await _polls.GetActivePollAsync().ConfigureAwait(false);
        if (active == null)
        {
            return ("❌ No active poll to end.", null, false);
        }

        var (ok, _, endMessage) = await _polls.EndPollAsync(active.Id).ConfigureAwait(false);
        if (!ok)
        {
            return ($"❌ {endMessage}", null, true);
        }

        return ($"✅ Poll ended. {endMessage}", null, true);
    }

    // ------------------------------------------------------------------
    // role assignment
    // ------------------------------------------------------------------

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleAssignModAsync(
        string[] args, ModChannelMessage message)
    {
        if (args.Length < 2)
        {
            return ("❌ Usage: !mod assignmod <userIdOrName> <TrialMod|Mod|SeniorMod|Admin>", null, false);
        }

        var target = NormalizeTarget(args[0]);
        if (target.Length == 0)
        {
            return ("❌ Usage: !mod assignmod <userIdOrName> <TrialMod|Mod|SeniorMod|Admin>", null, false);
        }

        var canonicalTier = ModTiers.NormalizeTier(args[1]);
        if (canonicalTier == null)
        {
            return ($"❌ Unknown tier '{args[1]}'. Valid tiers: {string.Join(", ", ModTiers.TierNames)}.", null, false);
        }

        try
        {
            await _modRoles.AssignAsync(target, target, canonicalTier, message.Username ?? "").ConfigureAwait(false);
            return ($"✅ {target} now holds the {canonicalTier} mod tier.", target, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign mod tier '{Tier}' to {Target}", canonicalTier, target);
            return ("❌ Failed to assign the mod tier.", target, false);
        }
    }

    // ------------------------------------------------------------------
    // delegated Discord moderation (kick/ban/mute/unban/timeout/unmute/purge/slowmode)
    // ------------------------------------------------------------------

    private async Task<(string? Reply, string? TargetUsername, bool Executed)> HandleDelegatedModerationAsync(
        string action, string[] args, ModChannelMessage message)
    {
        if (ModerationInvoker == null)
        {
            return ("❌ Moderation commands are not wired up.", null, false);
        }

        if (!DelegatedActions.Contains(action))
        {
            return ($"❌ Unknown mod action '{action}'.", null, false);
        }

        if (args.Length == 0)
        {
            return ($"❌ Usage: !mod {action} <args...>", null, false);
        }

        // The underlying Discord handler implements timeouts as its "mute"
        // command (timeout in minutes); "timeout" is this engine's alias.
        var underlying = action == "timeout" ? "mute" : action;
        var commandLine = "!" + underlying + " " + string.Join(" ", args);

        var response = await ModerationInvoker(message.BotId ?? "", commandLine, message.ChannelId).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response))
        {
            return ($"❌ '{action}' failed (is the bot connected and allowed to do that?).",
                TargetFromArgs(args), true);
        }

        return (response, TargetFromArgs(args), true);
    }

    // ------------------------------------------------------------------
    // small helpers
    // ------------------------------------------------------------------

    private static string NormalizeTarget(string raw)
        => (raw ?? "").Trim().TrimStart('@');

    private static string TargetFromArgs(string[] args)
    {
        var first = args.Length > 0 ? NormalizeTarget(args[0]) : "";
        return first;
    }

    private static string BuildUsage()
    {
        // Admin is the cumulative full command list.
        return $"Usage: !mod <action> [args...] — actions: {string.Join(", ", ModTiers.Admin)}.";
    }

    private async Task ReplyAsync(IModDiscordBot bot, string channelId, string text)
    {
        try
        {
            await bot.SendToChannelAsync(channelId, text, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send a mod reply into channel {ChannelId}", channelId);
        }
    }
}
