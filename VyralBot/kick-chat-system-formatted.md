# Technical Analysis of Streamer.bot Kick Integration: WebSocket Architectures, API Endpoints, and Event Ingestion Protocols

## 1. Executive Summary

This technical research report provides an exhaustive analysis of the integration architecture between Streamer.bot—a premier broadcast automation tool—and Kick.com, a burgeoning live-streaming platform. The primary objective of this document is to deconstruct the mechanisms by which Streamer.bot ingests real-time chat data, platform events, and channel telemetry without reliance on traditional, server-centric webhook architectures.

The analysis conclusively identifies that the core of this integration relies on a persistent, full-duplex WebSocket connection utilizing the Pusher Protocol (v7). Unlike platform integrations that depend on server-sent hooks—which require public-facing URLs and SSL certificate management unsuitable for typical desktop-based software—Streamer.bot mimics the client-side architecture of a web browser. It connects directly to Kick's hosted Pusher clusters (specifically us2) using public application keys to subscribe to internal event channels.

The report details the specific operational parameters required for this connection, including the static Pusher Key (`eb1d5f283081a78b932c`), the event namespace topology (`App\Events\...`), and the REST API endpoints used for resolution and transmission. Furthermore, it delineates the authentication evolution from legacy cookie-based session hijacking to the robust OAuth 2.1 standard implemented in Streamer.bot v1.0.0, which grants secure, scoped access to the Kick Public API.

By synthesizing data from reverse-engineered documentation, client library implementations (KickLib, Kick.py), and network traffic analysis, this report offers a granular view of the `api/v2` and `api/v1` endpoints. It dissects the JSON payloads for chat messages, moderation events, and subscription notifications, providing a complete blueprint of the data flow that enables Streamer.bot's "ALL chat events" functionality.

## 2. The Ecosystem of Real-Time Messaging in Live Streaming

To understand the specific engineering choices made in the Streamer.bot-Kick integration, one must first contextualize the technological landscape of live-stream interaction. The modern broadcasting stack requires sub-second latency between a viewer's action (typing a message, sending a gift) and the broadcaster's automated response (overlay triggers, text-to-speech, sound effects).

### 2.1 The Latency Imperative and Protocol Selection

In the domain of live streaming, latency is the defining metric of quality. An event architecture must bridge the gap between the platform's backend and the broadcaster's local machine. Historically, three paradigms have dominated this space:

- **Internet Relay Chat (IRC)**: The foundational protocol used by Twitch. It is a persistent TCP connection optimized for text. While efficient, it lacks the flexibility to handle complex metadata (rich objects, gifts, raid data) without proprietary extensions (IRCv3 tags).

- **Webhooks (EventSub)**: A server-to-server model where the platform pushes HTTP POST requests to a registered URL when an event occurs. This is highly scalable for cloud-hosted bots but presents significant friction for desktop applications like Streamer.bot.

- **WebSockets (Pub/Sub)**: A persistent, bidirectional communication channel over a single TCP connection. This model allows the server to push rich JSON objects to the client immediately upon generation.

### 2.2 The Streamer.bot Architectural Philosophy

Streamer.bot acts as a local orchestrator. It runs on the user's streaming PC, interfacing directly with broadcasting software like OBS Studio. Because it operates behind the user's residential router and firewall (NAT), it does not have a static public IP address.

This topology renders the webhook model fundamentally impractical for the core chat loop. Implementing webhooks would require every user to configure port forwarding on their router or utilize a tunneling service (like ngrok or Cloudflare Tunnels) to expose a local port to the public internet. This introduces security risks and configuration complexity.

Therefore, the WebSocket model is the only viable architecture for a user-friendly desktop application. By initiating an outbound connection to Kick's servers (standard HTTPS/WSS port 443), Streamer.bot establishes a persistent tunnel through which Kick can push "ALL" chat events in real-time. This confirms the user's premise: the integration is indeed WebSocket-based, not webhook-based.

## 3. The Kick.com Transport Layer: Pusher Protocol Deep Dive

The technical backbone of Kick's chat system is Pusher, a managed WebSocket service that provides scalable real-time communication APIs. Kick utilizes Pusher Channels to broadcast events to thousands of connected clients (viewers and bots) simultaneously. Streamer.bot functions as one of these clients.

### 3.1 Connection Constants and Cluster Topology

The connection to Kick's chat infrastructure is not generic; it requires specific authentication keys and cluster definitions that tell the client where to connect. Through analysis of the Kick web client and reverse-engineered SDKs, the following immutable constants have been identified as the "keys to the castle" for the WebSocket connection.

| Parameter | Value | Description | Implication |
|-----------|-------|-------------|-------------|
| **Pusher Key** | `eb1d5f283081a78b932c` | The public API key identifying the Kick application within the Pusher ecosystem. | Hardcoded in Streamer.bot. This key is public knowledge but essential for the handshake. |
| **Cluster** | `us2` | The geographical region of the Pusher server (US East). | Determines the base URL for the WebSocket connection. |
| **Protocol** | `7` | The version of the Pusher wire protocol. | Ensures compatibility with the JSON message format used by the server. |
| **Client** | `js` or `csharp` | The identifier of the client library type. | Streamer.bot, being C#, may identify as csharp, though mimicking js often reduces fingerprinting. |

### 3.2 WebSocket Endpoint Construction

Using the parameters defined above, the full connection Uniform Resource Identifier (URI) is constructed. Streamer.bot initiates a Secure WebSocket (WSS) connection to this specific endpoint.

**Primary WebSocket URL:**
```
wss://ws-us2.pusher.com/app/eb1d5f283081a78b932c?protocol=7&client=js&version=8.4.0-rc2&flash=false
```

**Breakdown of Query Parameters:**
- `protocol=7`: Dictates the structure of the system events (e.g., connection established payloads).
- `client=js`: Identifies the client implementation. Streamer.bot likely masquerades as a standard JS client or uses a C# Pusher library that sets this value.
- `version=8.4.0-rc2`: The version of the Pusher client library. This may change over time but 8.4.0 is currently standard.
- `flash=false`: A legacy parameter explicitly disabling Flash fallbacks, standard in modern implementations.

### 3.3 The Handshake and Connection Lifecycle

The WebSocket connection follows a strict lifecycle managed by the Pusher protocol. Streamer.bot must implement this state machine to maintain a stable connection during a live broadcast.

**Step 1: TCP/TLS Connection:**
The client opens a socket to port 443 at `ws-us2.pusher.com`.

**Step 2: Connection Established Event:**
Immediately upon connection, the server sends a system event to confirm the handshake. Streamer.bot must parse this to obtain the `socket_id`.

**Payload:**
```json
{
  "event": "pusher:connection_established",
  "data": "{\"socket_id\":\"123456.789123\",\"activity_timeout\":120}"
}
```

- `socket_id`: A unique session identifier. This is required if Streamer.bot needs to authenticate for private channels (though chat is generally public).
- `activity_timeout`: The keep-alive duration (120 seconds).

**Step 3: The Heartbeat (Ping/Pong):**
To prevent the connection from being closed by intermediate routers or the server itself due to inactivity, Streamer.bot must send a ping packet if no data is received within the timeout window.

- **Client Send**: `{"event": "pusher:ping", "data": {}}`
- **Server Reply**: `{"event": "pusher:pong", "data": {}}`

## 4. Pre-Connection Logic: Channel Resolution and API Discovery

A WebSocket connection alone provides no data. To receive chat messages, Streamer.bot must subscribe to a specific "Channel" on the Pusher connection. However, Pusher channels for Kick are identified by a numeric `chatroom_id`, not the streamer's username or user ID.

This necessitates a "Resolution Phase" where Streamer.bot queries the HTTP API to translate a human-readable username (Slug) into the required numeric identifiers.

### 4.1 The Hierarchy of Kick Entities

Understanding the API requires distinguishing between three distinct entities that are often conflated:

- **User (user_id)**: The account entity (e.g., "streamerbot").
- **Channel (channel_id)**: The broadcasting entity associated with a user.
- **Chatroom (chatroom_id)**: The specific container for chat messages. This is the ID required for the WebSocket.

### 4.2 The Resolution Endpoint: api/v2/channels

Streamer.bot performs a GET request to the channel API to retrieve these IDs.

- **Endpoint**: `https://kick.com/api/v2/channels/{slug}`
- **Method**: GET
- **Headers**: Requires standard browser headers to pass Cloudflare checks (User-Agent, Accept).

**Response Analysis:**
```json
{
  "id": 18293,                // Channel ID
  "user_id": 19485,           // User ID
  "slug": "streamername",
  "chatroom": {
    "id": 89345,              // CHATROOM ID (Critical)
    "created_at": "2023-01-01T00:00:00Z",
    "updated_at": "2023-01-02T00:00:00Z",
    "chat_mode_old": "public_channel",
    "chat_mode": "public",
    "slow_mode": false,
    "subscribers_mode": false,
    "followers_mode": false
  }
}
```

**Critical Insight**: The `chatroom.id` (e.g., 89345) is extracted from this JSON. This integer is the only value that matters for the subsequent WebSocket subscription. The `channel_id` (18293) is used for other metadata like stream title updates, but not for chat.

### 4.3 Subscription Logic

Once the `chatroom_id` is resolved, Streamer.bot sends a `pusher:subscribe` event over the established WebSocket connection.

- **Event**: `pusher:subscribe`
- **Channel Syntax**: `chatrooms.{chatroom_id}.v2`

**Subscription Payload:**
```json
{
  "event": "pusher:subscribe",
  "data": {
    "auth": "",
    "channel": "chatrooms.89345.v2"
  }
}
```

**Note**: The `.v2` suffix is a mandatory versioning scheme introduced by Kick to separate legacy chat infrastructure from the current implementation. Omitting it results in a successful subscription to a silent channel.

## 5. Event Ingestion: Anatomy of a Packet

The core of the request is to understand how Streamer.bot gets "ALL" chat events. Once subscribed, the WebSocket stream becomes a firehose of JSON data. Streamer.bot acts as a parser, filtering these messages based on the event name field.

Kick uses the Laravel Echo broadcasting convention, meaning event names are namespaced with `App\Events`. Streamer.bot maps these namespaces to its internal trigger system (e.g., "On Chat Message", "On Gift", "On Ban").

### 5.1 The Generic Wrapper

Every message received over the WebSocket follows a generic Pusher structure. The specific Kick data is contained inside a stringified JSON string within the `data` field. This requires a "Double-Decode" process:

1. Decode the outer WebSocket frame.
2. Parse the `data` string into a JSON object.

**Outer Frame:**
```json
{
  "channel": "chatrooms.89345.v2",
  "event": "App\\Events\\ChatMessageEvent",
  "data": "{\"id\":\"...\",\"content\":\"...\"}" // Stringified JSON
}
```

### 5.2 App\Events\ChatMessageEvent (Chat Messages)

This event carries the standard text messages sent by users.

**Decoded Payload Structure:**
```json
{
  "id": "c9b8a1...uuid...",
  "chatroom_id": 89345,
  "content": "Hello Streamer!!pog",
  "type": "message",
  "created_at": "2023-10-27T10:00:00Z",
  "sender": {
    "id": 55555,
    "username": "ViewerOne",
    "slug": "viewerone",
    "identity": {
      "color": "#FF0000",
      "badges": []
    }
  },
  "metadata": {
    "original_sender": null,
    "reply_to": null
  }
}
```

**Streamer.bot Processing Logic:**
- **Command Parsing**: The `content` field is scanned for command prefixes (e.g., !).
- **Role Identification**: The `sender.identity.badges` array is iterated to assign permissions. If type equals `moderator` or `broadcaster`, Streamer.bot grants elevated privileges.
- **User Context**: The `sender.id` and `sender.username` are cached to populate user variables (`%user%`, `%userId%`).

### 5.3 App\Events\GiftedSubscriptionsEvent (Sub Gifts)

This event is triggered when a user purchases subscriptions for others.

**Key Payload Fields:**
- `gifter_username`: The user sending the gifts.
- `gift_count`: Integer representing the number of subs gifted (e.g., 5, 10, 100).
- `recipient_usernames`: An array of strings listing the beneficiaries.

**Streamer.bot Logic**: This triggers the "Community Gift" sub-actions, populating `%giftCount%` and firing alerts.

### 5.4 App\Events\SubscriptionEvent (New/Resub)

Standard subscriptions trigger this event.

**Key Payload Fields:**
- `username`: The subscriber.
- `months`: The consecutive months subscribed.
- `method`: The payment provider (e.g., Stripe).

### 5.5 Moderation Events

To maintain a synchronized chat state (e.g., for an on-screen chat overlay), Streamer.bot listens for deletion and ban events.

**App\Events\MessageDeletedEvent:**
- **Payload**: `{"id": "message-uuid", "message": {"id": "message-uuid", "deleted": true}}`
- **Action**: Remove the message with the matching UUID from the UI.

**App\Events\UserBannedEvent:**
- **Payload**: `{"id": "...", "user": {"id": 123, "username": "bad_actor"}, "admin": {"username": "mod_user"}, "duration": 300}`
- **Action**: Trigger "User Banned" event, potentially logging the ban.

**App\Events\UserUnbannedEvent**: Indicates the restoration of chat privileges.

### 5.6 Stream State Events

Though not strictly inside the "Chatroom" channel, Streamer.bot often subscribes to the `channel.{channel_id}` Pusher channel to receive state updates.

- **App\Events\StreamerIsLive**: Signals the start of a broadcast.
- **App\Events\StopStreamBroadcast**: Signals the end.

## 6. The Transmission Layer: Sending Messages via REST API

The WebSocket connection described above is receive-only. The Pusher protocol supports client events (where a client pushes data to other clients), but Kick restricts this for security reasons. Therefore, when Streamer.bot needs to send a message (e.g., a reply to a command), it must switch protocols from WebSocket to HTTP.

### 6.1 The Message Transmission Endpoint

The primary endpoint for sending chat messages is `api/v2/messages/send`.

- **Endpoint**: `https://kick.com/api/v2/messages/send/{chatroom_id}`
- **Method**: POST
- **Content-Type**: application/json
- **Authorization**: Bearer Token (OAuth) or Cookie (XSRF-TOKEN).

**Request Payload:**
```json
{
  "content": "This is a reply from Streamer.bot",
  "type": "message",
  "metadata": {
      "reply_to": "parent-message-uuid" // Optional: For threading
  }
}
```

### 6.2 Rate Limiting and Cloudflare Defense

A significant complexity in Kick integration is the platform's aggressive use of Cloudflare to prevent botting. This presents a challenge for the "Write" path.

**Challenge**: Standard HTTP requests from C# HttpClient are often blocked with a 403 Forbidden status or a Cloudflare CAPTCHA challenge.

**Solution (Streamer.bot Mechanism)**: Streamer.bot v1.0.0 and later utilize the Official Public API, which bypasses the Cloudflare "Under Attack" mode checks required for unofficial scraping. By using a valid OAuth 2.1 token, the requests are authenticated as "Authorized Apps" and are subject to API rate limits rather than DDoS mitigation heuristics.

## 7. Authentication Architectures: From Cookies to OAuth 2.1

The original request asks for "how it's done." The "how" has evolved significantly. Understanding both legacy and modern methods is crucial for a complete technical picture, though Streamer.bot now relies on the modern standard.

### 7.1 Legacy Method: Session Hijacking (Unofficial)

Before v1.0.0, integrations often required the user to log in to Kick via a browser, extract the `kick_session` cookie and the `XSRF-TOKEN` header, and manually input them into the bot. This method mimicked a browser user but was fragile; tokens expired quickly, and Cloudflare challenges would frequently invalidate the session.

### 7.2 Modern Method: OAuth 2.1 (Official)

Streamer.bot v1.0.0 implements the OAuth 2.1 Authorization Code Flow with PKCE (Proof Key for Code Exchange). This is the industry standard for securing native desktop applications.

**The Auth Flow:**
1. **Initiation**: Streamer.bot opens the system browser to `https://kick.com/oauth/authorize` with a `client_id` and a `redirect_uri` (usually `http://localhost:port` or a custom protocol handler).
2. **User Consent**: The user clicks "Accept" on the Kick website, granting permissions.
3. **Code Grant**: Kick redirects the browser back to the `redirect_uri` with a temporary `code`.
4. **Token Exchange**: Streamer.bot captures this code and makes a POST request to `https://api.kick.com/oauth/token` to exchange it for an `access_token` and a `refresh_token`.
5. **Secure Storage**: The tokens are encrypted and stored locally. The `access_token` is included in the `Authorization: Bearer <token>` header for all API calls (like sending messages).
6. **Refreshing**: When the `access_token` expires (typically after 1-2 hours), Streamer.bot uses the `refresh_token` to transparently request a new one without user interaction.

**Scopes:**
Streamer.bot requests specific permission scopes to function:
- `user:read`: To get the user's ID and slug.
- `channel:read`: To get the chatroom ID.
- `chat:write`: To send messages.
- `events:subscribe`: To enable webhook subscriptions (if used for redundant event capture).

## 8. Comprehensive API Endpoint Catalog

The following is a catalog of the API endpoints identified through research snippets and reverse engineering that Streamer.bot interacts with to provide full functionality. These are distinct from the WebSocket channels but are essential for the integration.

### 8.1 Chat & Messaging Endpoints

| Method | Endpoint | Description | Usage in Streamer.bot |
|--------|----------|-------------|----------------------|
| POST | `/api/v2/messages/send/{chatroom_id}` | Send a message. | Used for Send Message sub-actions and replies. |
| DELETE | `/api/v2/chatrooms/{chatroom_id}/messages/{message_id}` | Delete a message. | Used for Delete Message sub-actions. |
| GET | `/api/v2/channels/{slug}/chatroom` | Get chat settings. | Used to check slow mode/follower-only status. |
| GET | `/api/v2/channels/{slug}/chatroom/rules` | Get chat rules. | Displayed in internal setup wizards. |
| POST | `/api/v2/channels/{slug}/polls` | Create a poll. | Used for Create Poll sub-actions. |
| DELETE | `/api/v2/channels/{slug}/polls` | End a poll. | Used for End Poll sub-actions. |

### 8.2 Channel & User Endpoints

| Method | Endpoint | Description | Usage in Streamer.bot |
|--------|----------|-------------|----------------------|
| GET | `/api/v2/channels/{slug}` | Get channel metadata. | Critical: Resolves slug to chatroom_id. |
| GET | `/api/v2/users/{username}` | Get user profile. | Fetches avatars and bios for "User Info" cards. |
| GET | `/api/v2/channels/{slug}/livestream` | Get stream status. | Checks if stream is Live/Offline for state tracking. |
| GET | `/api/v2/channels/{slug}/leaderboards` | Get top gifters. | Used for "Top Gifter" variables. |

### 8.3 Moderation Endpoints

| Method | Endpoint | Description | Usage in Streamer.bot |
|--------|----------|-------------|----------------------|
| POST | `/api/v2/channels/{slug}/bans` | Ban a user. | Used for Ban User sub-actions. |
| DELETE | `/api/v2/channels/{slug}/bans/{username}` | Unban a user. | Used for Unban User sub-actions. |
| POST | `/api/v2/channels/{slug}/pinned-message` | Pin a message. | Used for Pin Message sub-actions. |

## 9. Client-Side Simulation vs. Server-Side Integration

A key distinction in this architecture is that Streamer.bot acts as a **Hybrid Client**.

- **For Reading (Chat)**: It simulates a standard web client (Viewer). It connects to the same public WebSocket feed that a Chrome browser uses. This ensures it sees exactly what the user sees with zero latency. It does not rely on server-side webhooks because the local desktop application cannot easily accept incoming HTTP connections from the web.

- **For Writing (Actions)**: It acts as an Authorized Application (Developer App). It uses the OAuth API to perform privileged actions like banning users or sending messages.

This duality allows Streamer.bot to offer the responsiveness of a browser source with the power of a developer tool.

### 9.1 The "Internal" vs. "Public" API Distinction

Research snippets indicate the existence of `api/internal/v1` endpoints. While early bots relied on these, Streamer.bot's move to v1.0.0 signifies a shift to the stable `api/v2` endpoints. This ensures long-term reliability. The "internal" endpoints are subject to change without notice (breaking the bot), whereas the "public" (v1/v2) endpoints are contractually stable for developers.

## 10. Conclusion

The Streamer.bot integration with Kick.com is a sophisticated engineering solution that circumvents the limitations of webhook-based architectures in desktop environments. By reverse-engineering the Pusher WebSocket protocol, Streamer.bot establishes a direct, real-time line to Kick's event bus, subscribing to the `chatrooms.{id}.v2` channel to ingest `App\Events\ChatMessageEvent` payloads.

It effectively bridges the gap between "Public API" (for sending data securely) and "Internal WebSocket" (for reading data fast). For any developer looking to replicate this functionality, the critical path involves:

1. Authenticating via OAuth 2.1 to obtain a Bearer token.
2. Resolving the target channel's `chatroom_id` via `GET /api/v2/channels/{slug}`.
3. Connecting to `ws-us2.pusher.com` with key `eb1d5f283081a78b932c`.
4. Subscribing to the chatroom channel.
5. Parsing the `App\Events` JSON payloads.

This architecture provides the "ALL chat events" capability requested, offering a superset of functionality compared to standard webhook implementations.

---

## Appendix: Getting Chat History

To retrieve the entire history of a chatroom, use the following API endpoint:

```bash
curl "https://web.kick.com/api/v1/chat/23862571/history" \
  -H "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" \
  -H "Accept: application/json, text/plain, */*" \
  -H "Accept-Language: en-US,en;q=0.9" \
  -H "Referer: https://kick.com/idlrp" \
  -H "Origin: https://kick.com"
```

Replace `23862571` with your chatroom ID obtained from the `/api/v2/channels/{slug}` endpoint.


## Appendix2: Dev Console

When pressing f12 while signed into kick checking chat history, 

HEADERS:

:authority
web.kick.com
:method
GET
:path
/api/v1/chat/23862571/history
:scheme
https
accept
application/json
accept-encoding
gzip, deflate, br, zstd
accept-language
en-US,en;q=0.9,en-CA;q=0.8
authorization
Bearer 301683129|OsoHzxOQIXj7Qx123Do0A69iSA1pMVeSG68AoTbz
cache-control
no-cache
cookie
__stripe_mid=b95b5e74-78f3-450e-b142-c3d3e4279e9f9f9ad5; cookie_preferences_set_v1=%7B%22state%22%3A%7B%22preferences%22%3A%7B%22necessary%22%3Atrue%2C%22functional%22%3Atrue%2C%22performance%22%3Atrue%2C%22targeting%22%3Atrue%2C%22userHasMadeChoice%22%3Atrue%7D%2C%22functionalEnabled%22%3Atrue%2C%22performanceEnabled%22%3Atrue%2C%22targetingEnabled%22%3Atrue%7D%2C%22version%22%3A0%7D; kick_session_id=019a9b71-cac5-7874-86d7-7fd7fa9c074e; _ga=GA1.1.781185391.1763544517; USER_LOCALE=en; intercom-id-per7qesc=d9bb3d1a-535a-462e-b04a-acab5e8ca1b5; intercom-session-per7qesc=; intercom-device-id-per7qesc=258e0e56-7879-436b-bc5b-655b59652cc6; _cfuvid=kBzcmbg.5lPN2JyPN4hLe6ZaUoEh2upzyUKz3JVBff8-1764942188059-0.0.1.1-604800000; _gcl_au=1.1.2064526470.1763544517.310496942.1764953680.1764953708; XSRF-TOKEN=eyJpdiI6IlhYeU5JWU5PSEpSNTZSTmRqeVJnc2c9PSIsInZhbHVlIjoiMWdYTmU4NXlOUmFjQUZIalZTS2tzUk02MUU3NFRwdGRYclcwQ2RiWHhTMWZGUVN3TUNnWXZEb1BudGZJT2pUM3hrUzcvbStmNm85TjJON3o2dTk0ZlFMK1BTczBhc2dVUG5nMSt4V2g0MTVQVWVhYXR1c2JYOXR6V21YenZzUWgiLCJtYWMiOiI0MWZkNDZjNTQ4ZjcxM2YzM2UyNjJlZGFhNmEzMTc1N2I4Yzg2MzVlYzMxMWMwZjgzYWUyZjk4OTc2NWI0MmRhIiwidGFnIjoiIn0%3D; kick_session=eyJpdiI6ImNpNXhsejZtYjFWSXJSVERndXhEVmc9PSIsInZhbHVlIjoiVTFIQ1VpbGkwc25Db0Z3VjNDck9LZEFhcTJzeDYvZklnL1Q5UTRmRGFObGhnWXZLQ01kUkJyN2RJK0xsZVNQMDBzaW44c2xzL3dNQm1WSjFndHpaekwxdE5SU3hjWjJvbkVBcE8yUmc3MWtDN3plallzSjk4bktaSGNLckx1SmgiLCJtYWMiOiI5NDUwYmIxOGI2ZjQ2ZDM3N2Y3YzA0NmRhOTAxMmJiNGIxMTgxYTc0Mjc1ODAxM2VhYWQzN2RjM2I2OWEwZTg4IiwidGFnIjoiIn0%3D; zIGlTasC1ABgctPoS8aO3qivR0kExOHhRt9tbKUM=eyJpdiI6InB0dmVJcWJ2L0lIQngxTDJkaG9vUHc9PSIsInZhbHVlIjoiSzJ6c2NCcmEwU3RkVkwvN3N2czZSM25rT3l2MytCemJXY293M01VWmE2WHFTemsvMU1EWFRITGpBQWo2K1cxRHF1Q2g5UUNaV3RDdU5sMy9rT2MzWG10cUlOQm1CODJ2WjZtTFZrS0F2T0N1OXhadUcwWDM4MFFiZDlmaThOVnlFaU5HYWpuaHlHbllVdlFpcG51VjRUWnBjdHhFbTN2ZkdSb1NQWCtDdFppZitzZ0lmOU5FUldzY3VtUzN2SnFHMjBMLytvbVF1L0FGeDJINzhySXYrbnRsY1RyMmtiNmo0SittOTJPNjkzdGFoR1ErQ1N6Sk5FMFcxT1JyeGRuWW9YOHRPZVJHb2J2dFBhRzJIUkRHMEZzcUo0RVVpN3E1OXV6U0I0RVpXN3RwaVlkY2xLY2N1M3owY2U1eWdUSnZkOFQ4V3VkNEg5ZHAyQVE4VGw0NTVhaEszTlJIci95OW1janJsL2IzMXpOTzhZTDZCRjZxVVRiblJtVkN3T0FWdVpCLzQrQzVRS3RsOU02T1FuaGZ1OEtXWWRFSUFuc3dzSDAzZy9lbG12TDFHVTNocjFjQ1JwK1VlNTBpaDNJMzQ5NGZORjBPeTd0eExpclREYkxjUFJoVXYzU0MrbFg5amo4L0lkMXZnek94WHJ3dXl5T1JUdUtjOEJlQUk1MVZVc0RIbXVZR3MvdmNqTnNmRVFqR3AvdjA5RGtEbC80TFY5ZmJNSTdFOGhHcE9BbndaVnkyV3cvSnUrMytVZXhuWEF4MEJlRmlyQUoxTDFpZFV4Y3BFZz09IiwibWFjIjoiMDA0MGMyMGNjMjBiNjY5YWRiZmU0ZTUzMDg0YWQxNWU1ZmEwZmExNTc1NjM0Y2Y1NWU1NzM1ZGE0YjVhZDk4NSIsInRhZyI6IiJ9; session_token=301683129%7COsoHzxOQIXj7Qx123Do0A69iSA1pMVeSG68AoTbz; _iidt=FVwvvOPPkx6rjID83rLq6n18eJPQd8y26va8dUclxNuZjmRtKUho83ICocCpzmnkJmTmsaXIBtJB5+ER/twGA2yW6aBr0vcTqrrRe0Q=; _ga_46FFCLPHYQ=GS2.1.s1764968312$o13$g1$t1764969136$j58$l0$h0; __cf_bm=YenhGOhEXKYsiqQeqIPPEcltIl9cz4b57Z3M5p9miME-1764981155-1.0.1.1-3h7b28qeEYvCJ_zYgecTaytw6sdLlBRTJY7OyyE3mfn2PFV75vtZoF8m_1ZJ.z2RPkQ5Jonb4__DzNPHxuuMPv3aHwMugLA32368rv3y.sQ; cf_clearance=JdMgayAxftPiJK75dcA5nipq4IpSDntbsFzaMHgd0Jg-1764981155-1.2.1.1-UJDYRz6ya1MHY.Fz1p9UC7XE0wbtxfK.N6ZjXePZPKrQeE4Bka6.QSQwXpBLJUQ4506pEAlPMKLJNjLpGQb0SVSvCsOq4.UFoWEIYJrPuNk0u1gWbsEf3Bgi7NckANKdk2Ww.z0L1Emof9APpaGQteGzeaFDNYvF1t3H2A2qHcEGf2Mu1wFyKENupo7Pdi7eL_BsaQwxFB2i4OnanS_xSrFXeQqnCSvBVmp3cPEP4RQ; _ga_YS2DLSGQ53=GS2.1.s1764981155$o20$g0$t1764981156$j60$l0$h453848343; __stripe_sid=b40878cc-a527-482c-8f2e-69cc111a6b0a451ed6
dnt
1
origin
https://kick.com
pragma
no-cache
priority
u=1, i
referer
https://kick.com/
sec-ch-ua
"Chromium";v="142", "Microsoft Edge";v="142", "Not_A Brand";v="99"
sec-ch-ua-mobile
?0
sec-ch-ua-platform
"Windows"
sec-fetch-dest
empty
sec-fetch-mode
cors
sec-fetch-site
same-site
user-agent
Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0

PREVIEW/RESPONSE:

{
  "data": {
    "messages": [
      {
        "id": "6d53e529-b133-4353-8adc-04e85992de6f",
        "chat_id": 23862571,
        "user_id": 1160406,
        "content": "Come Check out our Discord! https://discord.com/invite/dripdropinn",
        "type": "message",
        "metadata": "null",
        "sender": {
          "id": 1160406,
          "slug": "botrix",
          "username": "BotRix",
          "identity": {
            "color": "#75FD46",
            "badges": [
              {
                "type": "moderator",
                "text": "Moderator"
              },
              {
                "type": "verified",
                "text": "Verified channel"
              }
            ]
          }
        },
        "created_at": "2025-12-05T23:20:53Z"
      },
      {
        "id": "6748c5ed-c746-459d-8a72-e0e19f77b608",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "https://gemini.google.com/share/2c19f410d00b",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764976286495\"}",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T23:11:27Z"
      },
      {
        "id": "77219b74-b1db-471a-ab38-176ce1bf76af",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "woah",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764976194216\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T23:09:55Z"
      },
      {
        "id": "fca79b37-7661-433e-ac21-c91ef7347453",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "ahh",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764976051255\"}",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T23:07:32Z"
      },
      {
        "id": "db910eaf-341f-40f1-b805-d9f7f549870e",
        "chat_id": 23862571,
        "user_id": 1160406,
        "content": "Come Check out our Discord! https://discord.com/invite/dripdropinn",
        "type": "message",
        "metadata": "null",
        "sender": {
          "id": 1160406,
          "slug": "botrix",
          "username": "BotRix",
          "identity": {
            "color": "#75FD46",
            "badges": [
              {
                "type": "moderator",
                "text": "Moderator"
              },
              {
                "type": "verified",
                "text": "Verified channel"
              }
            ]
          }
        },
        "created_at": "2025-12-05T23:07:28Z"
      },
      {
        "id": "7887ee52-ec0c-47e9-bdd0-58ecc0e6f166",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "boom",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764976021674\"}",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T23:07:02Z"
      },
      {
        "id": "16098a7f-b571-4649-9f4d-3b02b849eb1e",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "hiii",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764969119294\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T21:12:00Z"
      },
      {
        "id": "7fba24d3-3d40-47c6-a99d-855cf5e69ca3",
        "chat_id": 23862571,
        "user_id": 1160406,
        "content": "Come Check out our Discord! https://discord.com/invite/dripdropinn",
        "type": "message",
        "metadata": "null",
        "sender": {
          "id": 1160406,
          "slug": "botrix",
          "username": "BotRix",
          "identity": {
            "color": "#75FD46",
            "badges": [
              {
                "type": "moderator",
                "text": "Moderator"
              },
              {
                "type": "verified",
                "text": "Verified channel"
              }
            ]
          }
        },
        "created_at": "2025-12-05T21:07:26Z"
      },
      {
        "id": "0bb0fb33-9578-4a47-ab9c-db3d85302666",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "brosif",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764968837476\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T21:07:18Z"
      },
      {
        "id": "8c8d5bf7-a6f3-4952-b091-0f91956a2739",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "hi",
        "type": "message",
        "metadata": "[]",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T21:05:58Z"
      },
      {
        "id": "82aab4d9-985e-4cfe-9fdf-3be3e4550dcf",
        "chat_id": 23862571,
        "user_id": 1160406,
        "content": "Come Check out our Discord! https://discord.com/invite/dripdropinn",
        "type": "message",
        "metadata": "null",
        "sender": {
          "id": 1160406,
          "slug": "botrix",
          "username": "BotRix",
          "identity": {
            "color": "#75FD46",
            "badges": [
              {
                "type": "moderator",
                "text": "Moderator"
              },
              {
                "type": "verified",
                "text": "Verified channel"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:35:52Z"
      },
      {
        "id": "fcc0c9a7-e855-422d-84b9-e1cdc6751888",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "yesss",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764963304534\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T19:35:05Z"
      },
      {
        "id": "2144329c-8196-462c-beb8-67c2a5e84ee2",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "yeee",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764963226145\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T19:33:47Z"
      },
      {
        "id": "f7f8dfe4-83f3-4824-9316-bdae612d1b8f",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "VyralBot test message - Setup successful! 🎉",
        "type": "message",
        "metadata": "[]",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:33:33Z"
      },
      {
        "id": "6e7626a7-53a9-4cb4-a720-c1c87e4dadb7",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "testtt",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764962720277\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T19:25:21Z"
      },
      {
        "id": "03d45395-a854-4a7c-94fa-1e97bb097601",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "test",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764962713188\"}",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:25:14Z"
      },
      {
        "id": "bb1a41cb-d75e-4d13-ad94-3d53f4213114",
        "chat_id": 23862571,
        "user_id": 1160406,
        "content": "Come Check out our Discord! https://discord.com/invite/dripdropinn",
        "type": "message",
        "metadata": "null",
        "sender": {
          "id": 1160406,
          "slug": "botrix",
          "username": "BotRix",
          "identity": {
            "color": "#75FD46",
            "badges": [
              {
                "type": "moderator",
                "text": "Moderator"
              },
              {
                "type": "verified",
                "text": "Verified channel"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:20:52Z"
      },
      {
        "id": "8bea91a5-38f1-4ba5-ade5-9f6803a61dc8",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "raw",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764962382637\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T19:19:43Z"
      },
      {
        "id": "bcc3f80a-37b3-4d05-9dad-d4a6ba9c653d",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "VyralBot test message - Setup successful! 🎉",
        "type": "message",
        "metadata": "[]",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:18:55Z"
      },
      {
        "id": "20f2e041-a93a-464b-a2de-889d9a18ab20",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "too",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764961979431\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T19:13:00Z"
      },
      {
        "id": "91231ec4-a25f-4081-abd1-0301c5ec1390",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "sending from app for test",
        "type": "message",
        "metadata": "[]",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:12:46Z"
      },
      {
        "id": "c483c1dc-bcba-42af-8497-846a85a5e297",
        "chat_id": 23862571,
        "user_id": 1160406,
        "content": "Come Check out our Discord! https://discord.com/invite/dripdropinn",
        "type": "message",
        "metadata": "null",
        "sender": {
          "id": 1160406,
          "slug": "botrix",
          "username": "BotRix",
          "identity": {
            "color": "#75FD46",
            "badges": [
              {
                "type": "moderator",
                "text": "Moderator"
              },
              {
                "type": "verified",
                "text": "Verified channel"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:05:52Z"
      },
      {
        "id": "dacba87a-73d7-48d9-83fe-39cc60a9d0bd",
        "chat_id": 23862571,
        "user_id": 88485683,
        "content": "2",
        "type": "message",
        "metadata": "{\"message_ref\":\"1764961267312\"}",
        "sender": {
          "id": 88485683,
          "slug": "izlrp",
          "username": "izlrp",
          "identity": {
            "color": "#FF9D00",
            "badges": []
          }
        },
        "created_at": "2025-12-05T19:01:08Z"
      },
      {
        "id": "4f8909df-9423-4622-8773-81cb6039523e",
        "chat_id": 23862571,
        "user_id": 24865197,
        "content": "1",
        "type": "message",
        "metadata": "[]",
        "sender": {
          "id": 24865197,
          "slug": "idlrp",
          "username": "iDlrp",
          "identity": {
            "color": "#B9D6F6",
            "badges": [
              {
                "type": "broadcaster",
                "text": "Broadcaster"
              }
            ]
          }
        },
        "created_at": "2025-12-05T19:00:59Z"
      }
    ],
    "cursor": "1764960819765963",
    "pinned_message": null
  },
  "message": "OK"
}


