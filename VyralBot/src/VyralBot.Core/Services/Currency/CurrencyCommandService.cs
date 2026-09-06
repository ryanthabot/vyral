using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using VyralBot.Core.Services.Linking;
using VyralBot.Data;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for handling currency-related chat commands.
/// </summary>
public class CurrencyCommandService : ICurrencyCommandService
{
    private readonly ILogger<CurrencyCommandService> _logger;
    private readonly ICurrencyService _currencyService;
    private readonly ICurrencyRewardsService _rewardsService;
    private readonly ICurrencyGameService _gameService;
    private readonly ILoyaltyTierService _loyaltyTierService;
    private readonly ICurrencyBoostService _boostService;
    private readonly ICurrencyAuctionService _auctionService;
    private readonly ICurrencyBankService _bankService;
    private readonly ILinkCodeService _linkCodeService;
    private readonly IViewerStatusService _viewerStatusService;
    private readonly ISocialGoalService _socialGoalService;
    private readonly IMultiPlatformBonusService _multiPlatformBonusService;
    private readonly IDataService _dataService;

    public CurrencyCommandService(
        ILogger<CurrencyCommandService> logger,
        ICurrencyService currencyService,
        ICurrencyRewardsService rewardsService,
        ICurrencyGameService gameService,
        ILoyaltyTierService loyaltyTierService,
        ICurrencyBoostService boostService,
        ICurrencyAuctionService auctionService,
        ICurrencyBankService bankService,
        ILinkCodeService linkCodeService,
        IViewerStatusService viewerStatusService,
        ISocialGoalService socialGoalService,
        IMultiPlatformBonusService multiPlatformBonusService,
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
        _rewardsService = rewardsService ?? throw new ArgumentNullException(nameof(rewardsService));
        _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
        _loyaltyTierService = loyaltyTierService ?? throw new ArgumentNullException(nameof(loyaltyTierService));
        _boostService = boostService ?? throw new ArgumentNullException(nameof(boostService));
        _auctionService = auctionService ?? throw new ArgumentNullException(nameof(auctionService));
        _bankService = bankService ?? throw new ArgumentNullException(nameof(bankService));
        _linkCodeService = linkCodeService ?? throw new ArgumentNullException(nameof(linkCodeService));
        _viewerStatusService = viewerStatusService ?? throw new ArgumentNullException(nameof(viewerStatusService));
        _socialGoalService = socialGoalService ?? throw new ArgumentNullException(nameof(socialGoalService));
        _multiPlatformBonusService = multiPlatformBonusService ?? throw new ArgumentNullException(nameof(multiPlatformBonusService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task<string?> ProcessCommandAsync(string command, string username, string platform, bool isModerator)
    {
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith("!"))
            return null;

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLower();

        try
        {
            return cmd switch
            {
                "!points" or "!balance" or "!currency" => await HandlePointsCommandAsync(username, platform, parts),
                "!leaderboard" or "!top" => await HandleLeaderboardCommandAsync(parts),
                "!give" => isModerator ? await HandleGiveCommandAsync(parts, platform) : "You must be a moderator to use !give",
                "!take" => isModerator ? await HandleTakeCommandAsync(parts, platform) : "You must be a moderator to use !take",
                "!redeem" => await HandleRedeemCommandAsync(username, platform, parts),
                "!rewards" => await HandleRewardsCommandAsync(),
                "!gamble" or "!bet" => await HandleGambleCommandAsync(username, platform, parts),
                "!daily" => await HandleDailyCommandAsync(username, platform),
                "!rank" or "!tier" => await HandleRankCommandAsync(username, platform),
                "!store" or "!shop" => await HandleStoreCommandAsync(parts),
                "!slots" => await HandleSlotsCommandAsync(username, platform, parts),
                "!duel" => await HandleDuelCommandAsync(username, platform, parts),
                "!accept" => await HandleAcceptDuelCommandAsync(username, platform),
                "!heist" => await HandleHeistCommandAsync(username, platform, parts),
                "!roulette" => await HandleRouletteCommandAsync(username, platform, parts),
                "!link" => await HandleLinkCommandAsync(username, platform, parts),
                "!linkcode" => await HandleLinkRedeemCommandAsync(username, platform, parts),
                "!linked" => await HandleLinkedCommandAsync(username, platform),
                "!unlink" => LinkMessages.UnlinkReplacedByDelink,
                "!delink" => await HandleDelinkCommandAsync(username, platform, parts),
                "!goals" => await HandleGoalsCommandAsync(username, platform),
                "!mystatus" => await HandleMyStatusCommandAsync(username, platform),
                "!mp" or "!multiplatform" => await HandleMultiPlatformCommandAsync(username, platform, parts),
                "!boost" => isModerator ? await HandleBoostCommandAsync(username, platform, parts) : "You must be a moderator to use !boost",
                "!bid" => await HandleBidCommandAsync(username, platform, parts),
                "!auction" => isModerator ? await HandleAuctionCommandAsync(username, platform, parts) : "You must be a moderator to use !auction",
                "!deposit" => await HandleDepositCommandAsync(username, platform, parts),
                "!withdraw" => await HandleWithdrawCommandAsync(username, platform, parts),
                "!bank" => await HandleBankCommandAsync(username, platform),
                _ => null // Not a currency command
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing currency command: {Command}", command);
            return "An error occurred while processing your command.";
        }
    }

    private async Task<string> HandlePointsCommandAsync(string username, string platform, string[] parts)
    {
        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        // Check another user's balance (!points @username)
        if (parts.Length > 1)
        {
            var targetUser = parts[1].TrimStart('@');
            var balance = await _currencyService.GetBalanceAsync(targetUser, platform);
            return $"{targetUser} has {balance:N0} {currencyName}";
        }

        // Check own balance
        var userBalance = await _currencyService.GetBalanceAsync(username, platform);
        return $"@{username}, you have {userBalance:N0} {currencyName}";
    }

    private async Task<string> HandleLeaderboardCommandAsync(string[] parts)
    {
        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var limit = 5; // Default top 5
        if (parts.Length > 1 && int.TryParse(parts[1], out var requestedLimit))
        {
            limit = Math.Min(requestedLimit, 10); // Max 10
        }

        var leaderboard = await _currencyService.GetLeaderboardAsync(limit);

        if (!leaderboard.Any())
        {
            return $"No one has earned any {currencyName} yet!";
        }

        var lines = new List<string> { $"Top {limit} {currencyName} Leaders:" };
        int rank = 1;
        foreach (var user in leaderboard)
        {
            var medal = rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"#{rank}"
            };
            lines.Add($"{medal} {user.Username} - {user.Balance:N0}");
            rank++;
        }

        return string.Join(" | ", lines);
    }

    private async Task<string> HandleGiveCommandAsync(string[] parts, string platform)
    {
        if (parts.Length < 3)
        {
            return "Usage: !give @username amount";
        }

        var targetUser = parts[1].TrimStart('@');
        if (!int.TryParse(parts[2], out var amount) || amount <= 0)
        {
            return "Amount must be a positive number.";
        }

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var success = await _currencyService.AddCurrencyAsync(
            targetUser, platform, amount, $"Gift from moderator", "Gift");

        if (success)
        {
            var newBalance = await _currencyService.GetBalanceAsync(targetUser, platform);
            return $"✅ Gave {amount:N0} {currencyName} to @{targetUser} (New balance: {newBalance:N0})";
        }

        return "Failed to give currency.";
    }

    private async Task<string> HandleTakeCommandAsync(string[] parts, string platform)
    {
        if (parts.Length < 3)
        {
            return "Usage: !take @username amount";
        }

        var targetUser = parts[1].TrimStart('@');
        if (!int.TryParse(parts[2], out var amount) || amount <= 0)
        {
            return "Amount must be a positive number.";
        }

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var success = await _currencyService.RemoveCurrencyAsync(
            targetUser, platform, amount, $"Removed by moderator", "Penalty");

        if (success)
        {
            var newBalance = await _currencyService.GetBalanceAsync(targetUser, platform);
            return $"✅ Removed {amount:N0} {currencyName} from @{targetUser} (New balance: {newBalance:N0})";
        }

        return $"Failed to remove currency. User may not have enough {currencyName}.";
    }

    private async Task<string> HandleRedeemCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !redeem RewardName [optional input text]";
        }

        var rewardName = parts[1];
        var userInput = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : null;

        // Find reward by name (case-insensitive)
        var rewards = await _rewardsService.GetRewardsAsync(enabledOnly: true);
        var reward = rewards.FirstOrDefault(r =>
            r.Name.Equals(rewardName, StringComparison.OrdinalIgnoreCase));

        if (reward == null)
        {
            return $"Reward '{rewardName}' not found. Use !rewards to see available rewards.";
        }

        // Check if user can redeem
        var (canRedeem, reason) = await _rewardsService.CanRedeemAsync(reward.Id, username, platform);
        if (!canRedeem)
        {
            return $"Cannot redeem '{reward.Name}': {reason}";
        }

        // Attempt redemption
        var redemptionId = await _rewardsService.RedeemRewardAsync(reward.Id, username, platform, userInput);

        if (redemptionId != null)
        {
            var settings = await _currencyService.GetSettingsAsync();
            var currencyName = settings?.CurrencyName ?? "Points";
            var approvalText = reward.RequiresApproval
                ? " (pending moderator approval)"
                : " ✅";
            return $"@{username} redeemed '{reward.Name}' for {reward.Cost:N0} {currencyName}{approvalText}";
        }

        return "Failed to redeem reward.";
    }

    private async Task<string> HandleRewardsCommandAsync()
    {
        var rewards = await _rewardsService.GetRewardsAsync(enabledOnly: true);

        if (!rewards.Any())
        {
            return "No rewards available yet.";
        }

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var lines = new List<string> { "Available Rewards:" };
        foreach (var reward in rewards.Take(5)) // Show first 5
        {
            var cooldownText = reward.Cooldown > 0 ? $" (Cooldown: {reward.Cooldown}s)" : "";
            lines.Add($"{reward.Name} - {reward.Cost:N0} {currencyName}{cooldownText}");
        }

        if (rewards.Count() > 5)
        {
            lines.Add($"...and {rewards.Count() - 5} more");
        }

        return string.Join(" | ", lines);
    }

    private async Task<string> HandleGambleCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !gamble <amount> or !gamble all";
        }

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var currentBalance = await _currencyService.GetBalanceAsync(username, platform);

        int amount;
        if (parts[1].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            amount = currentBalance;
        }
        else if (!int.TryParse(parts[1], out amount) || amount <= 0)
        {
            return "Amount must be a positive number or 'all'.";
        }

        if (amount > currentBalance)
        {
            return $"You don't have enough {currencyName}! (Balance: {currentBalance:N0})";
        }

        // Enforce game settings (enable flag, min/max bet, balance) exactly like
        // !slots/!duel/!heist/!roulette. The 'Gamble' row is not part of the default
        // seed, so fall back to the legacy hardcoded 50/50 (min 10) only when the row
        // is missing entirely.
        var gameSetting = await _dataService.GetCurrencyGameSettingAsync("Gamble");
        if (gameSetting != null)
        {
            var (canPlay, reason) = await _gameService.CanPlayAsync(username, platform, "Gamble", amount);
            if (!canPlay)
            {
                return $"@{username}, {reason}";
            }

            // Games resolved outside CurrencyGameService must register their own
            // cooldown, otherwise !gamble can be spammed faster than !slots etc.
            _gameService.MarkPlayed(username, platform, "Gamble");
        }
        else if (amount < 10)
        {
            return $"Minimum bet is 10 {currencyName}.";
        }

        // 50% chance to win
        var random = new Random();
        var won = random.Next(0, 2) == 1;

        if (won)
        {
            // Double their bet
            await _currencyService.AddCurrencyAsync(username, platform, amount, "Gamble win", "Gamble");
            var newBalance = await _currencyService.GetBalanceAsync(username, platform);
            return $"🎰 @{username} won {amount:N0} {currencyName}! New balance: {newBalance:N0}";
        }
        else
        {
            // Lose their bet
            await _currencyService.RemoveCurrencyAsync(username, platform, amount, "Gamble loss", "Gamble");
            var newBalance = await _currencyService.GetBalanceAsync(username, platform);
            return $"🎰 @{username} lost {amount:N0} {currencyName}. New balance: {newBalance:N0}";
        }
    }

    private async Task<string> HandleDailyCommandAsync(string username, string platform)
    {
        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var (success, amount, streak, message) = await _currencyService.ClaimDailyBonusAsync(username, platform);

        if (!success)
        {
            return $"@{username}, {message}";
        }

        var streakText = streak > 1 ? $" (🔥 {streak} day streak!)" : "";
        return $"@{username} claimed {amount:N0} {currencyName}{streakText}";
    }

    private async Task<string> HandleRankCommandAsync(string username, string platform)
    {
        var tier = await _loyaltyTierService.GetUserTierAsync(username, platform);
        var nextTier = await _loyaltyTierService.GetNextTierAsync(username, platform);

        if (tier == null)
        {
            return $"@{username}, you don't have a rank yet. Start earning to rank up!";
        }

        var icon = !string.IsNullOrEmpty(tier.Icon) ? $"{tier.Icon} " : "";
        var response = $"@{username}, your rank: {icon}{tier.Name} ({tier.EarnMultiplier:0.#}x multiplier)";

        if (nextTier != null)
        {
            var userCurrency = await _currencyService.GetBalanceAsync(username, platform);
            // We need total earned, not current balance - approximate from tier context
            response += $" | Next: {nextTier.Name} at {nextTier.MinPoints:N0} total earned";
        }

        return response;
    }

    private async Task<string> HandleStoreCommandAsync(string[] parts)
    {
        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        int page = 1;
        string? category = null;

        if (parts.Length > 1)
        {
            if (int.TryParse(parts[1], out var parsedPage))
            {
                page = parsedPage;
            }
            else
            {
                category = parts[1];
                if (parts.Length > 2 && int.TryParse(parts[2], out var catPage))
                {
                    page = catPage;
                }
            }
        }

        var (rewards, totalPages) = await _rewardsService.GetStorePageAsync(page, 5, category);
        var rewardList = rewards.ToList();

        if (!rewardList.Any())
        {
            return category != null
                ? $"No rewards found in category '{category}'."
                : "The store is empty!";
        }

        var lines = new List<string>();
        var header = category != null
            ? $"📦 Store [{category}] (Page {page}/{totalPages}):"
            : $"📦 Store (Page {page}/{totalPages}):";
        lines.Add(header);

        foreach (var reward in rewardList)
        {
            var tierBadge = reward.RewardTier switch
            {
                "Rare" => "💙",
                "Epic" => "💜",
                "Legendary" => "💛",
                _ => ""
            };
            lines.Add($"{tierBadge}{reward.Name} — {reward.Cost:N0} {currencyName}");
        }

        return string.Join(" | ", lines);
    }

    private async Task<string> HandleSlotsCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !slots <amount>";
        }

        if (!int.TryParse(parts[1], out var amount) || amount <= 0)
        {
            return "Amount must be a positive number.";
        }

        var (canPlay, reason) = await _gameService.CanPlayAsync(username, platform, "Slots", amount);
        if (!canPlay)
        {
            return $"@{username}, {reason}";
        }

        var result = await _gameService.PlaySlotsAsync(username, platform, amount);
        return $"@{username} {result.Message}";
    }

    private async Task<string> HandleDuelCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 3)
        {
            return "Usage: !duel @username <amount>";
        }

        var target = parts[1].TrimStart('@');
        if (!int.TryParse(parts[2], out var amount) || amount <= 0)
        {
            return "Amount must be a positive number.";
        }

        var (canPlay, reason) = await _gameService.CanPlayAsync(username, platform, "Duel", amount);
        if (!canPlay)
        {
            return $"@{username}, {reason}";
        }

        var result = await _gameService.InitiateDuelAsync(username, platform, target, amount);
        return result;
    }

    private async Task<string> HandleAcceptDuelCommandAsync(string username, string platform)
    {
        var result = await _gameService.AcceptDuelAsync(username, platform);
        return result.Message;
    }

    private async Task<string> HandleHeistCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !heist <amount>";
        }

        if (!int.TryParse(parts[1], out var amount) || amount <= 0)
        {
            return "Amount must be a positive number.";
        }

        var (canPlay, reason) = await _gameService.CanPlayAsync(username, platform, "Heist", amount);
        if (!canPlay)
        {
            return $"@{username}, {reason}";
        }

        var result = await _gameService.JoinHeistAsync(username, platform, amount);
        return result;
    }

    private async Task<string> HandleRouletteCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 3)
        {
            return "Usage: !roulette <amount> <red/black/number>";
        }

        if (!int.TryParse(parts[1], out var amount) || amount <= 0)
        {
            return "Amount must be a positive number.";
        }

        var choice = parts[2].ToLower();

        var (canPlay, reason) = await _gameService.CanPlayAsync(username, platform, "Roulette", amount);
        if (!canPlay)
        {
            return $"@{username}, {reason}";
        }

        var result = await _gameService.PlayRouletteAsync(username, platform, amount, choice);
        return $"@{username} {result.Message}";
    }

    private async Task<string> HandleLinkCommandAsync(string username, string platform, string[] parts)
    {
        if (!await IsLinkingEnabledAsync())
        {
            return "🔗 Linking is currently disabled.";
        }

        // Redeem path: !link <code>
        if (parts.Length > 1)
        {
            if (!IsDigits(parts[1]))
            {
                return "Usage: !link — get a code, or !link <code> to redeem.";
            }

            return await RedeemLinkCodeAsync(parts[1], username, platform);
        }

        // Generate path: !link
        try
        {
            var (code, expiresAt) = await _linkCodeService.GenerateCodeAsync(username, platform);
            var minutes = Math.Max(1, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalMinutes));
            if (string.Equals(platform, "Discord", StringComparison.OrdinalIgnoreCase))
            {
                // The code is redeemed in ANOTHER platform's chat, so DMing
                // "!link <code>" back to the Discord bot is meaningless — drop
                // the DM sentence on Discord only; other platforms keep it.
                return $"🔗 Your link code: **{code}** (expires in {minutes} min). Send it from another platform:\n```\n!link {code}\n```";
            }
            return $"🔗 Your link code: {code} (expires in {minutes} min). Send it from another platform: type '!link {code}' in that platform's chat, or DM it to the Discord bot.";
        }
        catch (InvalidOperationException ex)
        {
            // LinkCodeService throws when linking is disabled for this platform
            return ex.Message;
        }
    }

    private async Task<string> HandleLinkRedeemCommandAsync(string username, string platform, string[] parts)
    {
        if (!await IsLinkingEnabledAsync())
        {
            return "🔗 Linking is currently disabled.";
        }

        if (parts.Length < 2 || !IsDigits(parts[1]))
        {
            return "Usage: !linkcode <code> — redeem the 6-digit code you generated on another platform.";
        }

        return await RedeemLinkCodeAsync(parts[1], username, platform);
    }

    private async Task<string> RedeemLinkCodeAsync(string code, string username, string platform)
    {
        // LinkRedeemResult.Message is a ready-to-send chat reply
        var result = await _linkCodeService.RedeemCodeAsync(code, username, platform);
        return result.Message;
    }

    private async Task<string> HandleLinkedCommandAsync(string username, string platform)
    {
        if (!await IsLinkingEnabledAsync())
        {
            return "🔗 Linking is currently disabled.";
        }

        var names = await GetLinkedPlatformNamesAsync(username, platform);
        if (names.Count == 0)
        {
            return "No linked accounts yet — type !link to start";
        }

        return $"🔗 Linked platforms: {string.Join(", ", names)} ({names.Count})";
    }

    private async Task<string> HandleDelinkCommandAsync(string username, string platform, string[] parts)
    {
        if (!await IsLinkingEnabledAsync())
        {
            return "🔗 Linking is currently disabled.";
        }

        // Redeem path: !delink <code> — typed on the account being removed.
        if (parts.Length > 1 && IsDigits(parts[1]))
        {
            var result = await _linkCodeService.RedeemDelinkCodeAsync(parts[1], username, platform);
            return result.Message;
        }

        // Generate path: !delink <platform> — typed from an account that stays.
        if (parts.Length > 1)
        {
            var targetPlatform = NormalizePlatformName(parts[1]);
            if (targetPlatform == null)
            {
                return "Supported platforms: twitch, kick, youtube, discord, tiktok, facebook, rumble";
            }

            try
            {
                var (code, expiresAt) = await _linkCodeService.GenerateDelinkCodeAsync(username, platform, targetPlatform);
                var minutes = Math.Max(1, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalMinutes));
                var delivery = LinkMessages.DelinkCodeDeliveryInstruction
                    .Replace("{code}", code)
                    .Replace("{platform}", targetPlatform)
                    .Replace("{n}", minutes.ToString());
                return $"🔗 Your delink code for {targetPlatform}: {code}. {delivery}";
            }
            catch (InvalidOperationException ex)
            {
                // LinkCodeService throws with a chat-ready message when removal
                // isn't possible (no linked accounts, unknown platform, etc.)
                return ex.Message;
            }
        }

        return LinkMessages.DelinkUsageHelp;
    }

    private async Task<string> HandleGoalsCommandAsync(string username, string platform)
    {
        var goals = (await _socialGoalService.GetGoalsAsync()).Where(g => g.IsEnabled).ToList();
        if (goals.Count == 0)
        {
            return "🏆 No social goals configured yet.";
        }

        var progress = await GetViewerProgressAsync(username, platform);
        var lines = new List<string> { "🏆 Social Goals:" };
        foreach (var goal in goals)
        {
            var met = progress.ViewerId != null && GoalEvaluator.IsMet(
                goal,
                progress.LinkedPlatformNames.Count,
                progress.FollowedPlatformCount,
                progress.SubbedPlatformCount,
                progress.BestSubTier);
            lines.Add($"{(met ? "✅" : "⬜")} {goal.Name}: {DescribeGoal(goal)}");
        }

        lines.Add($"📊 You: {progress.LinkedPlatformNames.Count} linked | {progress.FollowedPlatformCount} follows | {progress.SubbedPlatformCount} subs | best tier {progress.BestSubTier}");
        return string.Join(" | ", lines);
    }

    private async Task<string> HandleMyStatusCommandAsync(string username, string platform)
    {
        var progress = await GetViewerProgressAsync(username, platform);
        if (progress.ViewerId == null)
        {
            return "📊 No linked accounts yet — type !link to start linking your platforms.";
        }

        var multiplier = await _multiPlatformBonusService.GetMultiplierAsync(username, platform);

        var goals = (await _socialGoalService.GetGoalsAsync()).Where(g => g.IsEnabled).ToList();
        var goalsDone = goals.Count(g => GoalEvaluator.IsMet(
            g,
            progress.LinkedPlatformNames.Count,
            progress.FollowedPlatformCount,
            progress.SubbedPlatformCount,
            progress.BestSubTier));

        return $"📊 @{username}: linked {string.Join(", ", progress.LinkedPlatformNames)} ({progress.LinkedPlatformNames.Count})"
            + $" | 💰 Multiplier: {multiplier:0.##}x"
            + $" | 🏆 Goals completed: {goalsDone}/{goals.Count}";
    }

    private async Task<string> HandleMultiPlatformCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2
            || !(parts[1].Equals("on", StringComparison.OrdinalIgnoreCase)
                 || parts[1].Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            return "Usage: !mp on|off (alias: !multiplatform) — opt in or out of the multi-platform earnings bonus.";
        }

        // Only meaningful in OptIn mode (Auto applies to everyone, Off to no one)
        var modeRaw = (await _dataService.GetConfigValueAsync("MultiPlatform.Mode"))?.Trim();
        if (string.Equals(modeRaw, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return "ℹ️ The multi-platform bonus is automatic for linked viewers (Mode: Auto) — no opt-in needed.";
        }

        if (!string.Equals(modeRaw, "OptIn", StringComparison.OrdinalIgnoreCase))
        {
            return "ℹ️ The multi-platform bonus is currently disabled (Mode: Off).";
        }

        var optedIn = parts[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        if (optedIn)
        {
            // SetOptInAsync is a no-op for unlinked accounts — explain instead
            var names = await GetLinkedPlatformNamesAsync(username, platform);
            if (names.Count < 2)
            {
                return "🔗 Link another platform first with !link, then opt in to the multi-platform bonus.";
            }
        }

        await _multiPlatformBonusService.SetOptInAsync(username, platform, optedIn);
        return optedIn
            ? "✅ Multi-platform bonus enabled — your linked platforms now boost your earnings."
            : "❌ Multi-platform bonus disabled.";
    }

    /// <summary>
    /// Master gate for all !link-family commands: "Linking.Enabled" config
    /// (default true; an explicit "false" disables), matching the
    /// LinkCodeService "Linking.Platforms.{platform}" semantics.
    /// </summary>
    private async Task<bool> IsLinkingEnabledAsync()
    {
        var raw = await _dataService.GetConfigValueAsync("Linking.Enabled");
        return !bool.TryParse(raw, out var enabled) || enabled;
    }

    private static bool IsDigits(string value) => value.Length > 0 && value.All(char.IsDigit);

    private static string? NormalizePlatformName(string raw) => raw.ToLowerInvariant() switch
    {
        "twitch" => "Twitch",
        "kick" => "Kick",
        "youtube" => "YouTube",
        "discord" => "Discord",
        "tiktok" => "TikTok",
        "facebook" => "Facebook",
        "rumble" => "Rumble",
        _ => null
    };

    private async Task<List<string>> GetLinkedPlatformNamesAsync(string username, string platform)
    {
        var accounts = await _linkCodeService.GetLinkedAccountsForAsync(username, platform);
        return accounts
            .Select(a => a.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Aggregated per-viewer progress used by !goals and !mystatus: the unified
    /// viewer id (null while unlinked), distinct linked platform names, and the
    /// follow/sub/best-tier aggregates from the viewer-status rows.
    /// </summary>
    private async Task<ViewerProgress> GetViewerProgressAsync(string username, string platform)
    {
        var accounts = (await _linkCodeService.GetLinkedAccountsForAsync(username, platform)).ToList();
        var viewerId = accounts.FirstOrDefault()?.ViewerId;
        var linkedNames = accounts
            .Select(a => a.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (viewerId == null)
        {
            return new ViewerProgress(null, linkedNames, 0, 0, 0);
        }

        var statuses = (await _dataService.GetViewerStatusesByViewerIdAsync(viewerId)).ToList();
        var followed = statuses
            .Where(s => s.IsFollower)
            .Select(s => s.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var subbed = statuses
            .Where(s => s.IsSubscriber)
            .Select(s => s.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var bestTier = await _viewerStatusService.GetBestSubTierAcrossLinkedAsync(viewerId);

        return new ViewerProgress(viewerId, linkedNames, followed, subbed, bestTier);
    }

    private sealed record ViewerProgress(
        string? ViewerId,
        IReadOnlyList<string> LinkedPlatformNames,
        int FollowedPlatformCount,
        int SubbedPlatformCount,
        int BestSubTier);

    private static string DescribeGoal(VyralBot.Data.Entities.SocialGoal goal) => goal.GoalType switch
    {
        "LinkedPlatforms" => $"link {goal.TargetCount} platform(s)",
        "FollowPlatforms" => $"follow on {goal.TargetCount} platform(s)",
        "SubAnyPlatform" => $"subscribe on {goal.TargetCount} platform(s)",
        "SubTier" => $"reach sub tier {goal.TargetCount}",
        _ => !string.IsNullOrWhiteSpace(goal.Description) ? goal.Description : goal.Name
    };

    private async Task<string> HandleBoostCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 3)
        {
            return "Usage: !boost <multiplier> <minutes> [reason] (e.g., !boost 2 30 Happy Hour!)";
        }

        if (!double.TryParse(parts[1], out var multiplier) || multiplier < 1.0 || multiplier > 10.0)
        {
            return "Multiplier must be between 1.0 and 10.0";
        }

        if (!int.TryParse(parts[2], out var minutes) || minutes < 1 || minutes > 1440)
        {
            return "Duration must be 1-1440 minutes.";
        }

        var reason = parts.Length > 3 ? string.Join(" ", parts.Skip(3)) : "Streamer boost";

        var boost = await _boostService.StartBoostAsync(multiplier, minutes, reason, "Manual", platform);
        return $"🚀 {multiplier}x earning boost activated for {minutes} minutes! Reason: {reason}";
    }

    private async Task<string> HandleBidCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !bid <amount>";
        }

        if (!int.TryParse(parts[1], out var amount) || amount <= 0)
        {
            return "Bid amount must be a positive number.";
        }

        var (success, message) = await _auctionService.PlaceBidAsync(username, platform, amount);
        return message;
    }

    private async Task<string> HandleAuctionCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !auction start <item> <startbid> <minutes> | !auction end | !auction cancel | !auction info";
        }

        var subCommand = parts[1].ToLower();

        switch (subCommand)
        {
            case "start":
            {
                if (parts.Length < 5)
                    return "Usage: !auction start <item> <startbid> <minutes>";

                var itemName = parts[2];
                if (!int.TryParse(parts[3], out var startBid) || startBid <= 0)
                    return "Starting bid must be a positive number.";
                if (!int.TryParse(parts[4], out var minutes) || minutes < 1)
                    return "Duration must be at least 1 minute.";

                try
                {
                    var auction = await _auctionService.CreateAuctionAsync(itemName, null, startBid, minutes);
                    var settings = await _currencyService.GetSettingsAsync();
                    var currencyName = settings?.CurrencyName ?? "Points";
                    return $"🔨 Auction started! '{auction.ItemName}' — Starting bid: {startBid:N0} {currencyName}, Duration: {minutes}m. Use !bid <amount> to place a bid!";
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message;
                }
            }

            case "end":
            {
                var active = await _auctionService.GetActiveAuctionAsync();
                if (active == null)
                    return "No active auction to end.";
                var (resolved, message) = await _auctionService.ResolveAuctionAsync(active.Id);
                return message;
            }

            case "cancel":
            {
                var active = await _auctionService.GetActiveAuctionAsync();
                if (active == null)
                    return "No active auction to cancel.";
                var cancelled = await _auctionService.CancelAuctionAsync(active.Id);
                return cancelled ? "🔨 Auction cancelled. Bidder refunded." : "Failed to cancel auction.";
            }

            case "info":
            {
                var active = await _auctionService.GetActiveAuctionAsync();
                if (active == null)
                    return "No active auction right now.";
                var settings = await _currencyService.GetSettingsAsync();
                var currencyName = settings?.CurrencyName ?? "Points";
                var remaining = (int)(active.EndsAt - DateTime.UtcNow).TotalSeconds;
                var bidder = active.CurrentBidder ?? "No bids yet";
                return $"🔨 '{active.ItemName}' — Current bid: {active.CurrentBid:N0} {currencyName} by @{bidder} ({remaining}s remaining)";
            }

            default:
                return "Usage: !auction start|end|cancel|info";
        }
    }

    private async Task<string> HandleDepositCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !deposit <amount>";
        }

        int amount;
        if (parts[1].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            amount = await _currencyService.GetBalanceAsync(username, platform);
        }
        else if (!int.TryParse(parts[1], out amount) || amount <= 0)
        {
            return "Amount must be a positive number or 'all'.";
        }

        var (success, message) = await _bankService.DepositAsync(username, platform, amount);
        return success ? $"@{username} {message}" : $"@{username}, {message}";
    }

    private async Task<string> HandleWithdrawCommandAsync(string username, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: !withdraw <amount>";
        }

        int amount;
        if (parts[1].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var account = await _bankService.GetBankAccountAsync(username, platform);
            amount = account?.BankBalance ?? 0;
        }
        else if (!int.TryParse(parts[1], out amount) || amount <= 0)
        {
            return "Amount must be a positive number or 'all'.";
        }

        var (success, message) = await _bankService.WithdrawAsync(username, platform, amount);
        return success ? $"@{username} {message}" : $"@{username}, {message}";
    }

    private async Task<string> HandleBankCommandAsync(string username, string platform)
    {
        var account = await _bankService.GetBankAccountAsync(username, platform);
        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        if (account == null || account.BankBalance == 0)
        {
            return $"@{username}, your bank is empty. Use !deposit <amount> to save {currencyName}.";
        }

        var bankSettings = await _bankService.GetBankSettingsAsync();
        var interestText = bankSettings.DailyInterestRate > 0
            ? $" | Interest: {bankSettings.DailyInterestRate}%/day"
            : "";

        return $"🏦 @{username}'s Bank: {account.BankBalance:N0} {currencyName}{interestText} | Total interest earned: {account.TotalInterestEarned:N0}";
    }
}
