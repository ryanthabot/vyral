namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for currency mini-games (Slots, Duel, Heist, Roulette).
/// </summary>
public interface ICurrencyGameService
{
    /// <summary>
    /// Play the slots game.
    /// </summary>
    Task<GameResult> PlaySlotsAsync(string username, string platform, int amount);

    /// <summary>
    /// Initiate a duel challenge.
    /// </summary>
    Task<string> InitiateDuelAsync(string challenger, string platform, string opponent, int amount);

    /// <summary>
    /// Accept an incoming duel challenge.
    /// </summary>
    Task<GameResult> AcceptDuelAsync(string username, string platform);

    /// <summary>
    /// Start or join a heist.
    /// </summary>
    Task<string> JoinHeistAsync(string username, string platform, int amount);

    /// <summary>
    /// Resolve the current heist (called after join window closes).
    /// </summary>
    Task<HeistResult> ResolveHeistAsync(string platform);

    /// <summary>
    /// Play roulette with a color or number bet.
    /// </summary>
    Task<GameResult> PlayRouletteAsync(string username, string platform, int amount, string betType);

    /// <summary>
    /// Check if a user can play a game (cooldown check).
    /// </summary>
    Task<(bool CanPlay, string? Reason)> CanPlayAsync(string username, string platform, string gameType, int amount);

    /// <summary>
    /// Record that a user just played a game, starting its cooldown. Used by games
    /// resolved outside this service (e.g. !gamble) so their cooldown is enforced too.
    /// </summary>
    void MarkPlayed(string username, string platform, string gameType);
}

public record GameResult(bool Won, int AmountChange, string Message);
public record HeistResult(bool Resolved, int Participants, int Winners, string Message);
