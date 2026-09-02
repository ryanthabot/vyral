namespace VyralBot.Data.Entities;

/// <summary>
/// Per-game configuration for currency mini-games.
/// </summary>
public class CurrencyGameSetting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string GameType { get; set; } // "Slots", "Duel", "Heist", "Roulette"
    public bool IsEnabled { get; set; } = true;
    public int MinBet { get; set; } = 10;
    public int MaxBet { get; set; } = 10000;
    public int CooldownSeconds { get; set; } = 30;
    public double HouseEdgePercent { get; set; } = 5.0;
    public string? CustomConfig { get; set; } // JSON for game-specific settings
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
