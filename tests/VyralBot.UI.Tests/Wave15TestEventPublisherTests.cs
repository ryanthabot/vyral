using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Integrations.Testing;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-15 Event Testing seam: the <c>firstmessage.fire</c> test-event id must
/// be a registered <see cref="TestEventPublisher"/> type and must publish a
/// <see cref="FirstTimeChatterEvent"/> — the same event the live
/// FirstTimeChatterService detects — so welcome alerts/replies and the
/// "firstmessage" workflow key can be exercised from the Event Testing page
/// and <c>POST /api/events/test</c> without any database state (detection is
/// bypassed by construction: the event is published, not detected).
/// </summary>
public class Wave15TestEventPublisherTests
{
    private static TestEventPublisher Publisher(Wave15DispatchingEventBus bus) =>
        new(bus, NullLogger<TestEventPublisher>.Instance);

    [Fact]
    public void FirstMessageFireId_IsInTheSupportedRegistry()
    {
        Assert.Contains(TestEventPublisher.FirstMessage, TestEventPublisher.SupportedEventTypes);
        Assert.Equal("firstmessage.fire", TestEventPublisher.FirstMessage);

        // Registry invariants: unique ids, and the wave-15 id made it into the
        // list the Event Testing page and control API accept.
        Assert.Equal(
            TestEventPublisher.SupportedEventTypes.Count,
            TestEventPublisher.SupportedEventTypes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Publish_FirstMessageFire_MapsToFirstTimeChatterEvent_WithDefaults()
    {
        var bus = new Wave15DispatchingEventBus();

        await Publisher(bus).PublishTestEventAsync(TestEventPublisher.FirstMessage);

        var fired = Assert.IsType<FirstTimeChatterEvent>(Assert.Single(bus.Published));
        Assert.Equal("Twitch", fired.Platform);
        Assert.Equal("Twitch", fired.Source);
        Assert.Equal(TestEventPublisher.DefaultUsername, fired.Username);
        Assert.Equal(TestEventPublisher.DefaultMessage, fired.Message);
        Assert.Equal(TestEventPublisher.DefaultUsername, fired.DisplayName);
    }

    [Fact]
    public async Task Publish_FirstMessageFire_CustomUserAndMessage_MapThrough()
    {
        var bus = new Wave15DispatchingEventBus();

        await Publisher(bus).PublishTestEventAsync(TestEventPublisher.FirstMessage,
            username: "  NewViewer42  ", message: "  hello world  ");

        var fired = Assert.IsType<FirstTimeChatterEvent>(Assert.Single(bus.Published));
        Assert.Equal("NewViewer42", fired.Username); // publisher trims, service contract does not re-trim
        Assert.Equal("hello world", fired.Message);
        Assert.Equal("NewViewer42", fired.DisplayName);
    }

    [Fact]
    public async Task Publish_UnknownEventId_StillThrows()
    {
        var bus = new Wave15DispatchingEventBus();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Publisher(bus).PublishTestEventAsync("firstmessage.nope"));
    }
}
