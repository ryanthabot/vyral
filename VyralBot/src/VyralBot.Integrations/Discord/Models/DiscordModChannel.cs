namespace VyralBot.Integrations.Discord;

/// <summary>
/// Outcome of <see cref="IDiscordService.CreateModChannelAsync"/>: the
/// snowflake id of the (created or reused) private mod channel on success,
/// otherwise a human-readable error for the caller to surface.
/// </summary>
/// <param name="Success">True when the "vyral-mods" channel exists (created or reused).</param>
/// <param name="ChannelId">Snowflake ID of the channel; null on failure.</param>
/// <param name="Error">Human-readable failure reason; null on success.</param>
public record CreateModChannelResult(bool Success, string? ChannelId, string? Error);
