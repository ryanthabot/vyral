using VyralBot.Core.Services;
using Xunit;

namespace VyralBot.UI.Tests;

public class GiveawayOddsTests
{
    [Fact]
    public void Calculate_NoBoosts_ReturnsZeroBonusAndEmptyBreakdown()
    {
        var (bonusEntries, breakdown) = GiveawayOdds.Calculate(
            isSubscriberNow: false, subscriberBonusEntries: 2,
            bestLinkedTier: 0, t1Bonus: 1, t2Bonus: 2, t3Bonus: 3,
            followerAnywhere: false, followerBonus: 1,
            linkedPlatformCount: 1, perExtraPlatformBonus: 1, perExtraPlatformCap: 3);

        Assert.Equal(0, bonusEntries);
        Assert.Equal(string.Empty, breakdown);
    }

    [Fact]
    public void Calculate_SubscriberWithoutRecordedTier_EarnsTier1Bonus()
    {
        // Odds path: a current subscriber with no recorded tier counts as tier 1,
        // so they earn the tier-1 bonus (1), not the flat subscriber bonus (2).
        var (bonusEntries, breakdown) = GiveawayOdds.Calculate(
            isSubscriberNow: true, subscriberBonusEntries: 2,
            bestLinkedTier: 0, t1Bonus: 1, t2Bonus: 2, t3Bonus: 3,
            followerAnywhere: false, followerBonus: 1,
            linkedPlatformCount: 1, perExtraPlatformBonus: 1, perExtraPlatformCap: 3);

        Assert.Equal(1, bonusEntries);
        Assert.Equal("Tier 1 (+1)", breakdown);
    }

    [Fact]
    public void Calculate_OddsPath_IgnoresSubscriberBonusEntries()
    {
        // The flat subscriber bonus never stacks with the tier ladder on the odds
        // path: a tier-2 sub earns exactly the tier-2 bonus (2), never 2 + 2.
        var (bonusEntries, breakdown) = GiveawayOdds.Calculate(
            isSubscriberNow: true, subscriberBonusEntries: 99,
            bestLinkedTier: 2, t1Bonus: 1, t2Bonus: 2, t3Bonus: 3,
            followerAnywhere: false, followerBonus: 1,
            linkedPlatformCount: 1, perExtraPlatformBonus: 1, perExtraPlatformCap: 3);

        Assert.Equal(2, bonusEntries);
        Assert.Equal("Tier 2 (+2)", breakdown);

        // A non-subscriber earns no sub reward regardless of the flat bonus value.
        var (nonSubBonus, nonSubBreakdown) = GiveawayOdds.Calculate(
            isSubscriberNow: false, subscriberBonusEntries: 99,
            bestLinkedTier: 0, t1Bonus: 1, t2Bonus: 2, t3Bonus: 3,
            followerAnywhere: false, followerBonus: 1,
            linkedPlatformCount: 1, perExtraPlatformBonus: 1, perExtraPlatformCap: 3);

        Assert.Equal(0, nonSubBonus);
        Assert.Equal(string.Empty, nonSubBreakdown);
    }

    [Fact]
    public void Calculate_SubscriberWithLinkedTierAndBonuses_SumsLadderOnly()
    {
        // Spec example: Tier 2 (+2), Follower (+1), 3 linked platforms (+2) → 5.
        // The flat subscriber bonus (3) contributes nothing on the odds path.
        var (bonusEntries, breakdown) = GiveawayOdds.Calculate(
            isSubscriberNow: true, subscriberBonusEntries: 3,
            bestLinkedTier: 2, t1Bonus: 1, t2Bonus: 2, t3Bonus: 3,
            followerAnywhere: true, followerBonus: 1,
            linkedPlatformCount: 3, perExtraPlatformBonus: 1, perExtraPlatformCap: 3);

        Assert.Equal(5, bonusEntries);
        Assert.Equal("Tier 2 (+2), Follower (+1), 3 linked platforms (+2)", breakdown);
    }

    [Fact]
    public void Calculate_Tier3FollowerAndFourPlatforms_SumsBonusesAndBreakdown()
    {
        // Tier 3 (+3) + Follower (+1) + min(cap 3, 4-1) x 1 = (+3) → total 7.
        var (bonusEntries, breakdown) = GiveawayOdds.Calculate(
            isSubscriberNow: false, subscriberBonusEntries: 2,
            bestLinkedTier: 3, t1Bonus: 1, t2Bonus: 2, t3Bonus: 3,
            followerAnywhere: true, followerBonus: 1,
            linkedPlatformCount: 4, perExtraPlatformBonus: 1, perExtraPlatformCap: 3);

        Assert.Equal(7, bonusEntries);
        Assert.Contains("(+3)", breakdown);
        Assert.Equal("Tier 3 (+3), Follower (+1), 4 linked platforms (+3)", breakdown);
    }

    [Fact]
    public void Calculate_PlatformCountBeyondCap_ClampsPlatformBonus()
    {
        // 6 linked platforms → 5 extras, but capped at 3: platform part = 3 x 2 = 6.
        var (bonusEntries, breakdown) = GiveawayOdds.Calculate(
            isSubscriberNow: false, subscriberBonusEntries: 0,
            bestLinkedTier: 0, t1Bonus: 1, t2Bonus: 2, t3Bonus: 3,
            followerAnywhere: false, followerBonus: 0,
            linkedPlatformCount: 6, perExtraPlatformBonus: 2, perExtraPlatformCap: 3);

        Assert.Equal(6, bonusEntries);
        Assert.Equal("6 linked platforms (+6)", breakdown);
    }
}
