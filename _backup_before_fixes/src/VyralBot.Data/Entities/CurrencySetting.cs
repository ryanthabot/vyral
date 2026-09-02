namespace VyralBot.Data.Entities;

/// <summary>
/// Currency system configuration settings.
/// </summary>
public class CurrencySetting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CurrencyName { get; set; } = "Points";
    public string CurrencySymbol { get; set; } = "⭐";

    // Watch Time Rewards
    public bool WatchTimeEnabled { get; set; } = true;
    public int WatchTimeInterval { get; set; } = 60; // seconds
    public int WatchTimeAmount { get; set; } = 10;

    // Chat Activity Rewards
    public bool ChatActivityEnabled { get; set; } = true;
    public int ChatActivityCooldown { get; set; } = 120; // seconds
    public int ChatActivityAmount { get; set; } = 5;

    // Event Bonuses
    public bool FollowBonusEnabled { get; set; } = true;
    public int FollowBonusAmount { get; set; } = 100;

    public bool SubBonusEnabled { get; set; } = true;
    public int SubBonusAmount { get; set; } = 500;

    public bool GiftSubBonusEnabled { get; set; } = true;
    public int GiftSubBonusAmountPerSub { get; set; } = 50;

    public bool RaidBonusEnabled { get; set; } = true;
    public int RaidBonusAmountPerViewer { get; set; } = 10;

    // Phase 1: Cross-Platform Identity
    public bool UnifiedBalanceEnabled { get; set; }

    // Phase 2: Enhanced Earning
    public bool CheerBonusEnabled { get; set; } = true;
    public int CheerBonusAmountPerBit { get; set; } = 1;

    public bool SubMultiplierEnabled { get; set; }
    public double SubMultiplierValue { get; set; } = 2.0;

    public bool VipMultiplierEnabled { get; set; }
    public double VipMultiplierValue { get; set; } = 1.5;

    public bool ModMultiplierEnabled { get; set; }
    public double ModMultiplierValue { get; set; } = 1.5;

    public bool FirstChatBonusEnabled { get; set; } = true;
    public int FirstChatBonusAmount { get; set; } = 50;

    public bool DailyBonusEnabled { get; set; } = true;
    public int DailyBonusAmount { get; set; } = 100;

    public bool StreakBonusEnabled { get; set; } = true;
    public int StreakBonusDay1 { get; set; } = 50;
    public int StreakBonusDay5 { get; set; } = 100;
    public int StreakBonusDay10 { get; set; } = 250;

    public bool HostBonusEnabled { get; set; } = true;
    public int HostBonusAmount { get; set; } = 200;

    // Per-platform earning multipliers
    public double TwitchEarnMultiplier { get; set; } = 1.0;
    public double KickEarnMultiplier { get; set; } = 1.0;
    public double YouTubeEarnMultiplier { get; set; } = 1.0;

    // Phase 6: Economy Management
    public bool CurrencyDecayEnabled { get; set; }
    public double CurrencyDecayPercent { get; set; } = 5.0;
    public int CurrencyDecayDays { get; set; } = 30;

    // Phase 6: Banking System
    public bool BankEnabled { get; set; }
    public double BankInterestRate { get; set; } = 1.0; // % daily
    public int BankMaxBalance { get; set; } = 1000000;
    public double BankWithdrawalPenalty { get; set; } // % penalty
    public int BankWithdrawalCooldownMinutes { get; set; }

    // Phase 7: Transfer Tax
    public double TransferTaxPercent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
