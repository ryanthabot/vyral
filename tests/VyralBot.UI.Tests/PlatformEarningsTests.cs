using VyralBot.Core.Services.Currency;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// W2T5 per-platform earnings ledger rules:
///
///   1. Every earn/spend is ALSO recorded per platform in the PlatformEarnings
///      ledger (one row per (ViewerId, Platform)), because unified balances
///      zero per-platform UserCurrency rows and would otherwise lose the
///      per-platform history.
///   2. The ledger delta for an earn is (amount, 0); for a spend it is
///      (0, amount). Counters are monotonic — a delta never decreases them —
///      and they accumulate as long (an int-range award can push a counter
///      past int.MaxValue over a viewer's lifetime).
///
/// The increment semantics are defined by the pure static
/// <see cref="CurrencyService.PlatformLedgerMath.Apply"/> (the same read-add-write
/// math DapperDataService.AddPlatformEarningsAsync performs atomically in SQL)
/// and unit-tested directly here, following the WatchtimeGatingTests pattern:
/// the helper is public because VyralBot.Core does not grant InternalsVisibleTo
/// to the test project, and the test project has no mocking library, so the
/// testable unit is the isolated pure math.
/// </summary>
public class PlatformEarningsTests
{
    // ------------------------------------------------------------------
    // Earn deltas: (amount, 0)
    // ------------------------------------------------------------------

    [Fact]
    public void EarnedOnly_Delta_IncreasesEarned_LeavesSpent()
    {
        long earned = 0;
        long spent = 0;

        CurrencyService.PlatformLedgerMath.Apply(earnedDelta: 50, spentDelta: 0, ref earned, ref spent);

        Assert.Equal(50, earned);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void EarnedOnly_Delta_OnExistingRow_AddsToCurrentEarned()
    {
        long earned = 120;
        long spent = 30;

        CurrencyService.PlatformLedgerMath.Apply(earnedDelta: 15, spentDelta: 0, ref earned, ref spent);

        Assert.Equal(135, earned);
        Assert.Equal(30, spent);
    }

    // ------------------------------------------------------------------
    // Spend deltas: (0, amount)
    // ------------------------------------------------------------------

    [Fact]
    public void SpentOnly_Delta_IncreasesSpent_LeavesEarned()
    {
        long earned = 100;
        long spent = 0;

        CurrencyService.PlatformLedgerMath.Apply(earnedDelta: 0, spentDelta: 40, ref earned, ref spent);

        Assert.Equal(100, earned);
        Assert.Equal(40, spent);
    }

    // ------------------------------------------------------------------
    // Mixed + accumulation
    // ------------------------------------------------------------------

    [Fact]
    public void Mixed_Delta_AppliesBothCounters()
    {
        long earned = 10;
        long spent = 10;

        CurrencyService.PlatformLedgerMath.Apply(earnedDelta: 7, spentDelta: 3, ref earned, ref spent);

        Assert.Equal(17, earned);
        Assert.Equal(13, spent);
    }

    [Fact]
    public void Deltas_Accumulate_AcrossMultipleApplies()
    {
        // Simulates the atomic read-add-write sequence: each award re-reads the
        // row and adds its delta; repeated deltas must accumulate, never replace.
        long earned = 0;
        long spent = 0;

        CurrencyService.PlatformLedgerMath.Apply(25, 0, ref earned, ref spent);
        CurrencyService.PlatformLedgerMath.Apply(25, 0, ref earned, ref spent);
        CurrencyService.PlatformLedgerMath.Apply(0, 10, ref earned, ref spent);
        CurrencyService.PlatformLedgerMath.Apply(50, 5, ref earned, ref spent);

        Assert.Equal(100, earned);
        Assert.Equal(15, spent);
    }

    // ------------------------------------------------------------------
    // Ledger integrity
    // ------------------------------------------------------------------

    [Fact]
    public void NegativeDeltas_NeverDecreaseCounters()
    {
        // Counters are monotonic per component: a negative delta (a caller bug —
        // production only ever passes amount > 0) must be ignored, not subtracted.
        long earned = 100;
        long spent = 50;

        CurrencyService.PlatformLedgerMath.Apply(earnedDelta: -5, spentDelta: -9, ref earned, ref spent);

        Assert.Equal(100, earned);
        Assert.Equal(50, spent);
    }

    [Fact]
    public void LargeIntDeltas_AccumulateInLong_BeyondIntRange()
    {
        // The ledger columns are long: lifetime earnings can exceed int.MaxValue
        // even though each individual award fits an int.
        long earned = 0;
        long spent = 0;

        CurrencyService.PlatformLedgerMath.Apply(int.MaxValue, 0, ref earned, ref spent);
        CurrencyService.PlatformLedgerMath.Apply(int.MaxValue, 0, ref earned, ref spent);

        Assert.Equal(2L * int.MaxValue, earned);
        Assert.Equal(0, spent);
    }
}
