using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a conditional auto-reply rule with configurable conditions
/// </summary>
[Table("AutoReplyRules")]
public class AutoReplyRule
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Name of the rule for identification
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Platform this rule applies to (Twitch, Kick, Discord, or "All")
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Platform { get; set; } = "All";

    /// <summary>
    /// Event type this rule applies to (Follow, Subscribe, etc.)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this rule is currently active
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Priority order (lower number = higher priority)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// JSON-serialized condition configuration
    /// Format: { "type": "value|status|time", "field": "count", "operator": "==", "value": "100" }
    /// </summary>
    [Required]
    public string ConditionJson { get; set; } = "{}";

    /// <summary>
    /// The reply message template to send when conditions are met
    /// Supports variables like {username}, {count}, {platform}, etc.
    /// </summary>
    [Required]
    public string ReplyTemplate { get; set; } = string.Empty;

    /// <summary>
    /// When this rule was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this rule was last modified
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of times this rule has been triggered
    /// </summary>
    public int TriggerCount { get; set; } = 0;

    /// <summary>
    /// Last time this rule was triggered
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }
}
