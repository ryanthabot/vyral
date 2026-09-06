using VyralBot.Core.Services.Linking;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

public class GoalEvaluatorTests
{
    private static SocialGoal Goal(string goalType, int targetCount, string requiredPlatforms = "All")
    {
        return new SocialGoal
        {
            Name = "Test Goal",
            GoalType = goalType,
            TargetCount = targetCount,
            RequiredPlatforms = requiredPlatforms
        };
    }

    [Fact]
    public void IsMet_LinkedPlatforms_AtTargetCount_ReturnsTrue()
    {
        var goal = Goal("LinkedPlatforms", targetCount: 2);

        // Boundary equality: count == TargetCount counts as met.
        Assert.True(GoalEvaluator.IsMet(goal, linkedPlatformCount: 2, followedPlatformCount: 0, subbedPlatformCount: 0, bestSubTier: 0));
    }

    [Fact]
    public void IsMet_LinkedPlatforms_BelowTargetCount_ReturnsFalse()
    {
        var goal = Goal("LinkedPlatforms", targetCount: 2);

        Assert.False(GoalEvaluator.IsMet(goal, linkedPlatformCount: 1, followedPlatformCount: 5, subbedPlatformCount: 5, bestSubTier: 3));
    }

    [Fact]
    public void IsMet_FollowPlatforms_AtTargetCount_ReturnsTrue()
    {
        var goal = Goal("FollowPlatforms", targetCount: 3);

        Assert.True(GoalEvaluator.IsMet(goal, linkedPlatformCount: 0, followedPlatformCount: 3, subbedPlatformCount: 0, bestSubTier: 0));
    }

    [Fact]
    public void IsMet_FollowPlatforms_BelowTargetCount_ReturnsFalse()
    {
        var goal = Goal("FollowPlatforms", targetCount: 3);

        Assert.False(GoalEvaluator.IsMet(goal, linkedPlatformCount: 5, followedPlatformCount: 2, subbedPlatformCount: 5, bestSubTier: 3));
    }

    [Fact]
    public void IsMet_SubAnyPlatform_AtTargetCount_ReturnsTrue()
    {
        var goal = Goal("SubAnyPlatform", targetCount: 1);

        Assert.True(GoalEvaluator.IsMet(goal, linkedPlatformCount: 0, followedPlatformCount: 0, subbedPlatformCount: 1, bestSubTier: 0));
    }

    [Fact]
    public void IsMet_SubAnyPlatform_BelowTargetCount_ReturnsFalse()
    {
        var goal = Goal("SubAnyPlatform", targetCount: 2);

        Assert.False(GoalEvaluator.IsMet(goal, linkedPlatformCount: 5, followedPlatformCount: 5, subbedPlatformCount: 1, bestSubTier: 3));
    }

    [Fact]
    public void IsMet_SubTier_AtTargetCount_ReturnsTrue()
    {
        var goal = Goal("SubTier", targetCount: 2);

        Assert.True(GoalEvaluator.IsMet(goal, linkedPlatformCount: 0, followedPlatformCount: 0, subbedPlatformCount: 0, bestSubTier: 2));
    }

    [Fact]
    public void IsMet_SubTier_BelowTargetCount_ReturnsFalse()
    {
        var goal = Goal("SubTier", targetCount: 2);

        Assert.False(GoalEvaluator.IsMet(goal, linkedPlatformCount: 5, followedPlatformCount: 5, subbedPlatformCount: 5, bestSubTier: 1));
    }

    [Fact]
    public void IsMet_GoalTypeDifferentCase_IsMatched()
    {
        var goal = Goal("linkedplatforms", targetCount: 1);

        Assert.True(GoalEvaluator.IsMet(goal, linkedPlatformCount: 1, followedPlatformCount: 0, subbedPlatformCount: 0, bestSubTier: 0));
    }

    [Fact]
    public void IsMet_UnknownGoalType_ReturnsFalse()
    {
        var goal = Goal("WatchtimeHours", targetCount: 0);

        Assert.False(GoalEvaluator.IsMet(goal, linkedPlatformCount: 5, followedPlatformCount: 5, subbedPlatformCount: 5, bestSubTier: 3));
    }

    [Fact]
    public void IsMet_CsvRequiredPlatforms_DoesNotBlockCompletion()
    {
        // Actual code semantics: IsMet evaluates aggregate counts only;
        // RequiredPlatforms is not consulted (no per-platform filtering here).
        var goal = Goal("LinkedPlatforms", targetCount: 2, requiredPlatforms: "Twitch,Kick");

        Assert.True(GoalEvaluator.IsMet(goal, linkedPlatformCount: 2, followedPlatformCount: 0, subbedPlatformCount: 0, bestSubTier: 0));
    }
}
