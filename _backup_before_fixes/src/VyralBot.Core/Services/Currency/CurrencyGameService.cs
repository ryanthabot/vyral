using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for currency mini-games (Slots, Duel, Heist, Roulette).
/// </summary>
public class CurrencyGameService : ICurrencyGameService
{
    private readonly ILogger<CurrencyGameService> _logger;
    private readonly IDataService _dataService;
    private readonly ICurrencyService _currencyService;
    private readonly ConcurrentDictionary<string, DuelChallenge> _pendingDuels = new();
    private readonly ConcurrentDictionary<string, HeistSession> _activeHeists = new();
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    private static readonly string[] SlotSymbols = ["🍒", "🍋", "🔔", "⭐", "💎", "7️⃣"];

    private record DuelChallenge(string Challenger, string Opponent, string Platform, int Amount, DateTime ExpiresAt);
    private record HeistParticipant(string Username, int Amount);
    private record HeistSession(List<HeistParticipant> Participants, DateTime EndsAt);

    public CurrencyGameService(
        ILogger<CurrencyGameService> logger,
        IDataService dataService,
        ICurrencyService currencyService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
    }

    public async Task<(bool CanPlay, string? Reason)> CanPlayAsync(string username, string platform, string gameType, int amount)
    {
        var gameSetting = await _dataService.GetCurrencyGameSettingAsync(gameType);
        if (gameSetting == null || !gameSetting.IsEnabled)
        {
            return (false, $"{gameType} is currently disabled.");
        }

        if (amount < gameSetting.MinBet)
        {
            return (false, $"Minimum bet is {gameSetting.MinBet:N0}.");
        }

        if (amount > gameSetting.MaxBet)
        {
            return (false, $"Maximum bet is {gameSetting.MaxBet:N0}.");
        }

        var balance = await _currencyService.GetBalanceAsync(username, platform);
        if (balance < amount)
        {
            return (false, $"Insufficient balance (have {balance:N0}, need {amount:N0}).");
        }

        // Check cooldown
        var cooldownKey = $"{gameType}:{username}:{platform}";
        if (_cooldowns.TryGetValue(cooldownKey, out var lastPlayed))
        {
            var elapsed = DateTime.UtcNow - lastPlayed;
            if (elapsed.TotalSeconds < gameSetting.CooldownSeconds)
            {
                var remaining = (int)(gameSetting.CooldownSeconds - elapsed.TotalSeconds);
                return (false, $"On cooldown ({remaining}s remaining).");
            }
        }

        return (true, null);
    }

    private void SetCooldown(string username, string platform, string gameType)
    {
        var key = $"{gameType}:{username}:{platform}";
        _cooldowns[key] = DateTime.UtcNow;
    }

    public async Task<GameResult> PlaySlotsAsync(string username, string platform, int amount)
    {
        var (canPlay, reason) = await CanPlayAsync(username, platform, "Slots", amount);
        if (!canPlay)
        {
            return new GameResult(false, 0, reason!);
        }

        SetCooldown(username, platform, "Slots");

        // Spin 3 reels
        var reel1 = SlotSymbols[Random.Shared.Next(SlotSymbols.Length)];
        var reel2 = SlotSymbols[Random.Shared.Next(SlotSymbols.Length)];
        var reel3 = SlotSymbols[Random.Shared.Next(SlotSymbols.Length)];

        var display = $"[ {reel1} | {reel2} | {reel3} ]";

        int multiplier;
        if (reel1 == reel2 && reel2 == reel3)
        {
            // Jackpot: 3 of a kind
            multiplier = reel1 == "7️⃣" ? 10 : reel1 == "💎" ? 7 : 5;
        }
        else if (reel1 == reel2 || reel2 == reel3 || reel1 == reel3)
        {
            // Partial match: 2 of a kind
            multiplier = 2;
        }
        else
        {
            multiplier = 0; // Loss
        }

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        if (multiplier > 0)
        {
            var winnings = amount * multiplier;
            await _currencyService.AddCurrencyAsync(username, platform, winnings - amount, "Slots win", "Gamble");
            var newBalance = await _currencyService.GetBalanceAsync(username, platform);
            return new GameResult(true, winnings - amount,
                $"🎰 {display} — @{username} won {winnings:N0} {currencyName} ({multiplier}x)! Balance: {newBalance:N0}");
        }
        else
        {
            await _currencyService.RemoveCurrencyAsync(username, platform, amount, "Slots loss", "Gamble");
            var newBalance = await _currencyService.GetBalanceAsync(username, platform);
            return new GameResult(false, -amount,
                $"🎰 {display} — @{username} lost {amount:N0} {currencyName}. Balance: {newBalance:N0}");
        }
    }

    public async Task<string> InitiateDuelAsync(string challenger, string platform, string opponent, int amount)
    {
        var (canPlay, reason) = await CanPlayAsync(challenger, platform, "Duel", amount);
        if (!canPlay)
        {
            return reason!;
        }

        var key = $"{opponent.ToLowerInvariant()}:{platform}";

        if (_pendingDuels.ContainsKey(key))
        {
            return $"@{opponent} already has a pending duel. They must !accept or wait for it to expire.";
        }

        _pendingDuels[key] = new DuelChallenge(challenger, opponent, platform, amount, DateTime.UtcNow.AddSeconds(60));

        return $"⚔️ @{challenger} challenges @{opponent} to a duel for {amount:N0}! Type !accept within 60s to fight!";
    }

    public async Task<GameResult> AcceptDuelAsync(string username, string platform)
    {
        var key = $"{username.ToLowerInvariant()}:{platform}";

        if (!_pendingDuels.TryRemove(key, out var duel))
        {
            return new GameResult(false, 0, "You don't have any pending duel challenges.");
        }

        if (DateTime.UtcNow > duel.ExpiresAt)
        {
            return new GameResult(false, 0, "The duel challenge has expired.");
        }

        // Check both players have enough balance
        var opponentBalance = await _currencyService.GetBalanceAsync(username, platform);
        if (opponentBalance < duel.Amount)
        {
            return new GameResult(false, 0, $"@{username} doesn't have enough balance for the duel.");
        }

        var challengerBalance = await _currencyService.GetBalanceAsync(duel.Challenger, platform);
        if (challengerBalance < duel.Amount)
        {
            return new GameResult(false, 0, $"@{duel.Challenger} no longer has enough balance for the duel.");
        }

        SetCooldown(username, platform, "Duel");
        SetCooldown(duel.Challenger, platform, "Duel");

        // 50/50 outcome
        var challengerWins = Random.Shared.Next(2) == 0;
        var winner = challengerWins ? duel.Challenger : username;
        var loser = challengerWins ? username : duel.Challenger;

        await _currencyService.RemoveCurrencyAsync(loser, platform, duel.Amount, $"Duel loss vs {winner}", "Gamble");
        await _currencyService.AddCurrencyAsync(winner, platform, duel.Amount, $"Duel win vs {loser}", "Gamble");

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        return new GameResult(true, 0,
            $"⚔️ @{winner} defeated @{loser} in a duel and won {duel.Amount:N0} {currencyName}!");
    }

    public async Task<string> JoinHeistAsync(string username, string platform, int amount)
    {
        var (canPlay, reason) = await CanPlayAsync(username, platform, "Heist", amount);
        if (!canPlay)
        {
            return reason!;
        }

        var key = platform.ToLowerInvariant();
        var session = _activeHeists.GetOrAdd(key, _ => new HeistSession(new List<HeistParticipant>(), DateTime.UtcNow.AddSeconds(60)));

        if (DateTime.UtcNow > session.EndsAt)
        {
            // Start new heist
            session = new HeistSession(new List<HeistParticipant>(), DateTime.UtcNow.AddSeconds(60));
            _activeHeists[key] = session;
        }

        lock (session.Participants)
        {
            if (session.Participants.Any(p => p.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                return $"@{username}, you're already in the heist!";
            }
            session.Participants.Add(new HeistParticipant(username, amount));
        }

        // Deduct entry
        await _currencyService.RemoveCurrencyAsync(username, platform, amount, "Heist entry", "Gamble");

        var count = session.Participants.Count;
        var remaining = (int)(session.EndsAt - DateTime.UtcNow).TotalSeconds;

        return $"🏦 @{username} joined the heist with {amount:N0}! ({count} participants, {remaining}s to join)";
    }

    public async Task<HeistResult> ResolveHeistAsync(string platform)
    {
        var key = platform.ToLowerInvariant();

        if (!_activeHeists.TryRemove(key, out var session) || session.Participants.Count == 0)
        {
            return new HeistResult(false, 0, 0, "No active heist to resolve.");
        }

        var participants = session.Participants;
        var totalPot = participants.Sum(p => p.Amount);

        // Success chance scales with participants: base 30% + 5% per participant, max 80%
        var successChance = Math.Min(0.30 + (participants.Count * 0.05), 0.80);

        var winners = new List<HeistParticipant>();
        var losers = new List<HeistParticipant>();

        foreach (var p in participants)
        {
            if (Random.Shared.NextDouble() < successChance)
            {
                winners.Add(p);
            }
            else
            {
                losers.Add(p);
            }
        }

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        if (winners.Count == 0)
        {
            return new HeistResult(true, participants.Count, 0,
                $"🏦 The heist failed! All {participants.Count} participants lost their bets.");
        }

        // Distribute total pot evenly among winners (proportional to bet)
        var winnerTotalBet = winners.Sum(w => w.Amount);
        foreach (var winner in winners)
        {
            var share = (int)((double)winner.Amount / winnerTotalBet * totalPot);
            if (share > winner.Amount)
            {
                await _currencyService.AddCurrencyAsync(winner.Username, platform, share - winner.Amount, "Heist win", "Gamble");
            }
        }

        var winnerNames = string.Join(", ", winners.Select(w => $"@{w.Username}"));
        return new HeistResult(true, participants.Count, winners.Count,
            $"🏦 Heist complete! {winners.Count}/{participants.Count} survived! Winners: {winnerNames} split {totalPot:N0} {currencyName}!");
    }

    public async Task<GameResult> PlayRouletteAsync(string username, string platform, int amount, string betType)
    {
        var (canPlay, reason) = await CanPlayAsync(username, platform, "Roulette", amount);
        if (!canPlay)
        {
            return new GameResult(false, 0, reason!);
        }

        SetCooldown(username, platform, "Roulette");

        // Spin: 0-36
        var result = Random.Shared.Next(0, 37);
        var isRed = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 }.Contains(result);
        var isBlack = result > 0 && !isRed;
        var colorEmoji = result == 0 ? "🟢" : isRed ? "🔴" : "⚫";

        var settings = await _currencyService.GetSettingsAsync();
        var currencyName = settings?.CurrencyName ?? "Points";

        var betLower = betType.ToLowerInvariant();
        bool won;
        int multiplier;

        if (betLower == "red")
        {
            won = isRed;
            multiplier = 2;
        }
        else if (betLower == "black")
        {
            won = isBlack;
            multiplier = 2;
        }
        else if (int.TryParse(betType, out var betNumber) && betNumber >= 0 && betNumber <= 36)
        {
            won = result == betNumber;
            multiplier = 36;
        }
        else
        {
            return new GameResult(false, 0, "Invalid bet. Use: red, black, or a number 0-36.");
        }

        if (won)
        {
            var winnings = amount * multiplier;
            await _currencyService.AddCurrencyAsync(username, platform, winnings - amount, "Roulette win", "Gamble");
            var newBalance = await _currencyService.GetBalanceAsync(username, platform);
            return new GameResult(true, winnings - amount,
                $"🎡 {colorEmoji} {result}! @{username} won {winnings:N0} {currencyName} ({multiplier}x)! Balance: {newBalance:N0}");
        }
        else
        {
            await _currencyService.RemoveCurrencyAsync(username, platform, amount, "Roulette loss", "Gamble");
            var newBalance = await _currencyService.GetBalanceAsync(username, platform);
            return new GameResult(false, -amount,
                $"🎡 {colorEmoji} {result}! @{username} lost {amount:N0} {currencyName}. Balance: {newBalance:N0}");
        }
    }
}
