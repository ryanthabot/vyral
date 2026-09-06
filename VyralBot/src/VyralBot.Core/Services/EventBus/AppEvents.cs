using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.EventBus.Events;

/// <summary>
/// Well-known page keys used with <see cref="NavigateToPageEvent"/>.
/// </summary>
public static class AppPages
{
    public const string Giveaways = "giveaways";
    public const string Polls = "polls";
    public const string EventTesting = "eventtesting";
    public const string Settings = "settings";
    public const string Moderator = "moderator";
}

/// <summary>
/// Published by embedded surfaces (e.g. the chat quick-actions popup) to request
/// main-window navigation without holding a reference to MainViewModel.
/// MainViewModel subscribes and routes the key to its navigation commands.
/// </summary>
public record NavigateToPageEvent : EventBase
{
    /// <summary>One of the <see cref="AppPages"/> keys.</summary>
    public required string PageKey { get; init; }
}
