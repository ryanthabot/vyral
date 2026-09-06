using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Service managing broadcaster-defined social goals and per-viewer
/// completions for them.
/// </summary>
public interface ISocialGoalService
{
    /// <summary>
    /// Seeds the built-in default goals when no goal exists yet
    /// (idempotent — never runs when any goal is present).
    /// </summary>
    Task SeedDefaultsAsync();

    /// <summary>
    /// Evaluates every enabled goal for the viewer, completing and rewarding
    /// any goal whose condition is newly met (idempotent per GoalId+ViewerId).
    /// </summary>
    Task EvaluateViewerAsync(string viewerId, string username, string platform);

    /// <summary>
    /// Gets all social goals ordered by SortOrder.
    /// </summary>
    Task<IEnumerable<SocialGoal>> GetGoalsAsync();

    /// <summary>
    /// Inserts or updates a social goal.
    /// </summary>
    Task SaveGoalAsync(SocialGoal goal);

    /// <summary>
    /// Deletes a social goal (completions are kept).
    /// </summary>
    Task DeleteGoalAsync(Guid id);

    /// <summary>
    /// Number of viewers that completed the given goal.
    /// </summary>
    Task<int> GetCompletionCountAsync(Guid goalId);
}
