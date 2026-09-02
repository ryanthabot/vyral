# VyralBot Chat Events User Guide

## Overview
VyralBot automatically detects and displays chat events from your Kick and Twitch streams, including follows, subscriptions, raids, and more. You can customize how these events appear and set up automatic replies to thank your viewers!

---

## Supported Events

### Kick Events
- **Follow** - When someone follows your channel
- **Subscribe** - When someone subscribes (with month count)
- **Gift Subscription** - When someone gifts subs to the community
- **Host** - When another channel hosts you
- **Kicks Gifted** - When someone gifts Kick currency
- **Reward Redeemed** - When someone redeems a channel reward

### Twitch Events
- **Follow** - When someone follows your channel
- **Subscribe** - New subscriber (Tier 1/2/3)
- **Resub** - Returning subscriber with message
- **Gift Subscription** - When someone gifts subs
- **Raid** - When another channel raids you
- **Cheer** - When someone cheers bits
- **Channel Points** - When someone redeems channel points

---

## Event Display

Events appear in the chat window with special formatting:
- 🎨 **Colored background** to stand out from regular messages
- 🔔 **Icon indicators** for different event types
- 📊 **Event details** (follower count, sub tier, bits amount, etc.)

### Customizing Event Display

**To show/hide events:**
1. Navigate to **Settings** in the main menu
2. Select either **Kick Chat Settings** or **Twitch Chat Settings**
3. Scroll to **Chat Event Notifications** section
4. Toggle **Show in chat** for each event type

✅ **Enabled** = Event appears in chat  
❌ **Disabled** = Event is hidden (but still logged)

---

## Auto-Reply System

VyralBot can automatically thank viewers when they follow, subscribe, raid, etc. by sending a message to your chat!

### Setting Up Auto-Replies

1. Navigate to **Kick Chat Settings** or **Twitch Chat Settings**
2. Find the **Chat Event Notifications** section
3. For each event type:
   - ✅ Check **Enable auto-reply**
   - ✍️ Customize the **Auto-reply message** template

### Message Templates

Auto-reply messages support **variables** that get replaced with actual values:

| Variable | Description | Example |
|----------|-------------|---------|
| `{username}` | User's display name | "TestUser123" |
| `{count}` | Number (followers, subs, etc.) | "42" |
| `{months}` | Subscription months | "6" |
| `{message}` | User's message | "Love the stream!" |
| `{platform}` | Platform name | "Kick" or "Twitch" |
| `{viewers}` | Raid viewer count | "150" |
| `{bits}` | Bits amount | "100" |
| `{reward}` | Reward name | "Hydrate!" |
| `{tier}` | Sub tier | "1000" (Tier 1) |
| `{gifter}` | Gift sub sender | "GenerousViewer" |

### Example Auto-Reply Messages

**Follow:**
```
Thanks for the follow @{username}! Welcome to the community! 🎉
```

**Subscribe:**
```
Thank you @{username} for subscribing! You're awesome! ⭐ (Month {months})
```

**Gift Subs:**
```
Wow! @{gifter} just gifted {count} subs to the community! You're amazing! 🎁
```

**Raid:**
```
Thank you @{username} for the raid with {viewers} viewers! Welcome everyone! 🚀
```

**Cheer:**
```
Thanks for the {bits} bits @{username}! Much appreciated! 💎
```

---

## Rate Limiting

To prevent spam, auto-replies have a **30-second cooldown per user** per event type.

**Example:**
- User "TestUser" follows → Auto-reply sent ✅
- User "TestUser" follows again within 30 seconds → Auto-reply blocked ❌
- Wait 30 seconds → Auto-reply works again ✅

**Why this matters:**
- Prevents spam during bot attacks
- Avoids flooding chat during raids
- Keeps chat clean and readable

---

## Testing Events

VyralBot includes a **Developer Testing Tool** to manually trigger events without needing real platform connections!

### How to Test Events:

1. Navigate to **Event Testing** in the main menu
2. Set your **Test Username** (default: "TestUser123")
3. Set your **Test Message** (for events that include messages)
4. Click any event button to trigger that event
5. Watch it appear in the **Chat** window
6. Check the **Event Log** for confirmation

### Testing Auto-Replies:

1. Enable auto-reply for an event type (e.g., Follow)
2. Set a custom message template
3. Use the Event Testing Tool to trigger that event
4. Verify the auto-reply appears in chat with variables replaced
5. Trigger the same event again quickly (< 30 sec) → No auto-reply (rate limited)
6. Wait 30+ seconds → Trigger again → Auto-reply works

---

## Troubleshooting

### Events not appearing in chat?
1. Check if **Show in chat** is enabled for that event type
2. Verify your platform is connected (Kick or Twitch)
3. Check the **Event Log** to see if events are being received

### Auto-replies not sending?
1. Check if **Enable auto-reply** is toggled ON
2. Verify your message template is not empty
3. Check if you're rate-limited (30-second cooldown)
4. Verify your platform connection has send message permissions

### Wrong variables in auto-reply?
1. Check your template syntax: `{username}` not `{{username}}`
2. Use the Event Testing Tool to test your template
3. Refer to the variables table above for correct variable names

### Events appearing twice?
1. Make sure you're not connected to the same platform twice
2. Check for duplicate event handlers in settings
3. Restart VyralBot if issues persist

---

## Best Practices

### 📝 Writing Good Auto-Replies

✅ **DO:**
- Keep messages short and friendly
- Use emojis for personality (but not too many!)
- Include the viewer's username with `@{username}`
- Be genuine and appreciative
- Test your templates before going live

❌ **DON'T:**
- Make messages too long (< 200 characters recommended)
- Use excessive caps or punctuation (!!!!!!)
- Copy/paste the same message for all events
- Forget to use variables - personalization matters!
- Spam multiple responses per event

### 🎯 Event Visibility Strategy

Consider **hiding events** that are:
- Too frequent (might spam chat)
- Not relevant to your community
- Distracting during important moments

Consider **showing events** that are:
- Significant milestones (subs, raids, big donations)
- Community engagement moments
- Special achievements

### 🔊 Managing Chat Flow

During **high-traffic streams**:
- Consider disabling auto-replies for follows
- Keep auto-replies for high-value events only (subs, raids, etc.)
- Monitor chat spam and adjust settings

During **low-traffic streams**:
- Enable all auto-replies to engage viewers
- Show all event types to celebrate every interaction
- Be more verbose and welcoming

---

## Advanced Tips

### Multi-Platform Strategy

If streaming to both Kick and Twitch:
1. Set **different auto-reply messages** for each platform
2. Customize event visibility per platform
3. Use platform-specific emojis or inside jokes
4. Monitor both chats for best engagement

### Event Logging

All events are logged to the database, even if hidden from chat. Use the **Event History** viewer to:
- Review past events
- Export data for analytics
- Track follower/sub growth
- Identify your most engaged viewers

### Currency Integration (Future Feature)

Events can award currency to viewers automatically:
- Follows → +10 points
- Subs → +50 points
- Raids → +5 points per viewer
- Bits → +1 point per bit

Configure in **Currency Settings** (coming soon).

---

## FAQ

**Q: Do I need to be live for events to work?**  
A: No! Events work anytime you're connected to Kick/Twitch, even offline.

**Q: Can I use multiple auto-reply messages?**  
A: Currently, one message per event type. Conditional messages coming soon!

**Q: Are events stored permanently?**  
A: Yes, all events are logged to the database indefinitely.

**Q: Can I export event data?**  
A: Yes! Use the Event History viewer and click "Export to CSV".

**Q: What happens if auto-reply fails?**  
A: The error is logged but chat continues normally. No crashes or interruptions.

**Q: Can I test without connecting to platforms?**  
A: Yes! Use the **Event Testing** tool to trigger fake events.

**Q: Do auto-replies count against my message rate limit?**  
A: Auto-replies use the same API as manual messages, so yes, they count toward platform rate limits.

---

## Support

Need help?
- 📖 Read the full documentation at `/docs/`
- 🐛 Report bugs on [GitHub Issues](https://github.com/yourusername/vyralbot/issues)
- 💬 Join our Discord community (coming soon)
- 📧 Email support: support@vyralbot.com

---

*Last Updated: December 11, 2025 - Version 1.0*
