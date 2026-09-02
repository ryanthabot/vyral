# Kick API Complete Documentation

**Generated:** December 5, 2025  
**Project:** VyralBot  
**Purpose:** Complete reference for Kick.com Developer API integration

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [OAuth 2.1 Authentication](#oauth-21-authentication)
3. [Scopes](#scopes)
4. [Categories API](#categories-api)
5. [Users API](#users-api)
6. [Channels API](#channels-api)
7. [Channel Rewards API](#channel-rewards-api)
8. [Chat API](#chat-api)
9. [Moderation API](#moderation-api)
10. [Livestreams API](#livestreams-api)
11. [Public Key API](#public-key-api)
12. [KICKs (Gifting) API](#kicks-gifting-api)
13. [Webhooks & Events](#webhooks--events)
14. [Organizations & Drops](#organizations--drops)

---

## Getting Started

### Kick App Setup

**Steps to create a Kick Developer Application:**

1. **Create Kick Account**
   - Sign up at https://kick.com if you don't have an account
   - Review [Developer Terms and Conditions](https://kick.com/terms)

2. **Enable Two-Factor Authentication (2FA)**
   - Required to access developer tools
   - Go to Account Settings → Security → Enable 2FA

3. **Navigate to Developer Tab**
   - Go to [Account Settings → Developer](https://kick.com/settings/developer)

4. **Create Your App**
   - Click "Create Application"
   - Fill in details:
     - **Application Name:** e.g., "VyralBot"
     - **Redirect URI:** `http://localhost:3000/callback` (for local dev)
   - Save to generate:
     - **Client ID** (public identifier)
     - **Client Secret** (keep secure, never share)

5. **Note Your Credentials**
   - Save Client ID and Client Secret securely
   - These will be used for OAuth authentication

---

## OAuth 2.1 Authentication

### Base URLs

- **OAuth Server:** `https://id.kick.com`
- **API Server:** `https://api.kick.com/public/v1`

### Token Types

#### 1. App Access Token
- **Flow:** Client Credentials
- **Use Case:** Server-to-server, public data access
- **No user login required**

#### 2. User Access Token
- **Flow:** Authorization Code with PKCE
- **Use Case:** User-authorized actions (chat, subscriptions, etc.)
- **Requires user consent**

---

### Authorization Code Flow (User Access Token)

#### Step 1: Generate Authorization URL

**Endpoint:** `GET https://id.kick.com/oauth/authorize`

**Parameters:**
```
response_type: code
client_id: <your_client_id>
redirect_uri: <your_callback_url> (must match app settings)
scope: <space-separated scopes>
code_challenge: <PKCE code challenge>
code_challenge_method: S256
state: <random_value for CSRF protection>
```

**Example URL:**
```
https://id.kick.com/oauth/authorize?
  response_type=code&
  client_id=abc123...&
  redirect_uri=http://localhost:3000/callback&
  scope=chat:write+user:read+events:subscribe&
  code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&
  code_challenge_method=S256&
  state=random-csrf-token
```

**Response:**
User authorizes → Redirects to:
```
http://localhost:3000/callback?code=xyz789&state=random-csrf-token
```

---

#### Step 2: Exchange Code for Token

**Endpoint:** `POST https://id.kick.com/oauth/token`

**Headers:**
```
Content-Type: application/x-www-form-urlencoded
```

**Body Parameters:**
```
grant_type: authorization_code
client_id: <client_id>
client_secret: <client_secret>
redirect_uri: <same as Step 1>
code_verifier: <PKCE code verifier>
code: <authorization_code from Step 1>
```

**Example Request:**
```http
POST /oauth/token HTTP/1.1
Host: id.kick.com
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code&
client_id=abc123&
client_secret=secret456&
redirect_uri=http://localhost:3000/callback&
code_verifier=dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk&
code=xyz789
```

**Success Response (200):**
```json
{
  "access_token": "kick_token_abc123...",
  "token_type": "Bearer",
  "refresh_token": "kick_refresh_xyz789...",
  "expires_in": 3600,
  "scope": "chat:write user:read events:subscribe"
}
```

---

#### Step 3: Refresh Token

**Endpoint:** `POST https://id.kick.com/oauth/token`

**Body Parameters:**
```
grant_type: refresh_token
client_id: <client_id>
client_secret: <client_secret>
refresh_token: <refresh_token>
```

**Success Response (200):**
```json
{
  "access_token": "new_access_token",
  "token_type": "Bearer",
  "refresh_token": "new_refresh_token",
  "expires_in": 3600,
  "scope": "chat:write user:read events:subscribe"
}
```

---

### App Access Token (Server-to-Server)

**Endpoint:** `POST https://id.kick.com/oauth/token`

**Body Parameters:**
```
grant_type: client_credentials
client_id: <client_id>
client_secret: <client_secret>
```

**Success Response (200):**
```json
{
  "access_token": "app_token_abc123...",
  "expires_in": 3600,
  "token_type": "Bearer"
}
```

---

### Revoke Token

**Endpoint:** `POST https://id.kick.com/oauth/revoke`

**Query Parameters:**
```
token: <your_token>
token_hint_type: access_token | refresh_token
```

**Success Response:** `200 OK`

---

### 127.0.0.1 Workaround

**Issue:** NextJS framework bug changes first occurrence of `127.0.0.1` to `localhost`

**Solution:** Add sacrificial query parameter before `redirect_uri`:

```
https://id.kick.com/oauth/authorize?
  response_type=code&
  client_id=abc123&
  redirect=127.0.0.1&          ← Sacrificial parameter
  redirect_uri=http://127.0.0.1/callback&
  scope=chat:write&
  ...
```

---

## Scopes

**Required for User Access Tokens only (not App Access Tokens)**

| Scope | Title | Description |
|-------|-------|-------------|
| `channel:read` | View channel information | View channel description, category, etc. |
| `channel:write` | Update livestream metadata | Update stream title, category, tags |
| `channel:rewards:read` | Read channel points rewards | View rewards configuration |
| `channel:rewards:write` | Manage channel points rewards | Create, update, delete rewards |
| `chat:write` | Send chat messages | Send chat messages and bot messages |
| `events:subscribe` | Subscribe to webhooks | Subscribe to real-time events (chat, follows, subs) |
| `kicks:read` | View KICKs leaderboards | View gifting leaderboards |
| `moderation:ban` | Execute moderation actions | Ban/timeout users |
| `moderation:chat_message:manage` | Manage chat messages | Delete specific chat messages |
| `streamkey:read` | Read stream key | View stream URL and stream key |
| `user:read` | View user information | View username, streamer ID, email, profile |

---

## Categories API

### Get Categories (Search)

**Endpoint:** `GET https://api.kick.com/public/v1/categories`

**Authorization:** User Access Token (OAuth2)

**Query Parameters:**
- `q` (required): Search query
- `page` (optional): Page number (default: 1)

**Example Request:**
```http
GET /public/v1/categories?q=rust HTTP/1.1
Host: api.kick.com
Authorization: Bearer YOUR_OAUTH2_TOKEN
```

**Success Response (200):**
```json
{
  "data": [
    {
      "id": 101,
      "name": "Old School Runescape",
      "thumbnail": "https://kick.com/img/categories/old-school-runescape.jpeg"
    }
  ],
  "message": "OK"
}
```

---

### Get Category by ID

**Endpoint:** `GET https://api.kick.com/public/v1/categories/:category_id`

**Path Parameters:**
- `category_id` (int64): Category ID

**Success Response (200):**
```json
{
  "data": {
    "id": 101,
    "name": "Old School Runescape",
    "tags": ["pog", "chad"],
    "thumbnail": "https://kick.com/img/categories/old-school-runescape.jpeg",
    "viewer_count": 1000
  },
  "message": "OK"
}
```

---

## Users API

### Token Introspect

**Endpoint:** `POST https://api.kick.com/public/v1/token/introspect`

**Authorization:** User Access Token

**Description:** Get information about the current access token (OAuth 2.0 spec RFC 7662)

**Success Response (200):**
```json
{
  "data": {
    "active": true,
    "client_id": "0144Y845NBDSST5H2G1DVY2T25",
    "exp": 1719619200,
    "scope": "user:read chat:write moderation:ban",
    "token_type": "user"
  },
  "message": "OK"
}
```

**Inactive Token (200):**
```json
{
  "data": {
    "active": false
  },
  "message": "OK"
}
```

---

### Get Users

**Endpoint:** `GET https://api.kick.com/public/v1/users`

**Authorization:** User Access Token with `user:read` scope

**Query Parameters:**
- `id` (optional): Array of user IDs (int64)
- If no IDs provided, returns currently authenticated user

**Example Request (current user):**
```http
GET /public/v1/users HTTP/1.1
Host: api.kick.com
Authorization: Bearer YOUR_OAUTH2_TOKEN
```

**Example Request (specific users):**
```http
GET /public/v1/users?id=123&id=456 HTTP/1.1
Host: api.kick.com
Authorization: Bearer YOUR_OAUTH2_TOKEN
```

**Success Response (200):**
```json
{
  "data": [
    {
      "email": "john.doe@example.com",
      "name": "John Doe",
      "profile_picture": "https://kick.com/img/default-profile-pictures/default2.jpeg",
      "user_id": 123
    }
  ],
  "message": "OK"
}
```

---

## Channels API

### Get Channels

**Endpoint:** `GET https://api.kick.com/public/v1/channels`

**Authorization:** User Access Token with `channel:read` scope

**Query Parameters (mutually exclusive):**
- **Option 1:** No parameters → Returns authenticated user's channel
- **Option 2:** `broadcaster_user_id` (array, max 50) → Get channels by user IDs
- **Option 3:** `slug` (array, max 50, max 25 chars each) → Get channels by channel slugs

**⚠️ Cannot mix `broadcaster_user_id` and `slug` in same request**

**Example Request (current user):**
```http
GET /public/v1/channels HTTP/1.1
Host: api.kick.com
Authorization: Bearer YOUR_OAUTH2_TOKEN
```

**Example Request (by slug):**
```http
GET /public/v1/channels?slug=streamerjohn HTTP/1.1
Host: api.kick.com
Authorization: Bearer YOUR_OAUTH2_TOKEN
```

**Success Response (200):**
```json
{
  "data": [
    {
      "banner_picture": "https://kick.com/img/default-banner-pictures/default2.jpeg",
      "broadcaster_user_id": 123,
      "category": {
        "id": 101,
        "name": "Old School Runescape",
        "thumbnail": "https://kick.com/img/categories/old-school-runescape.jpeg"
      },
      "channel_description": "Channel description",
      "slug": "john-doe",
      "stream": {
        "custom_tags": ["tag1", "tag2"],
        "is_live": true,
        "is_mature": true,
        "key": "super-secret-stream-key",
        "language": "en",
        "start_time": "0001-01-01T00:00:00Z",
        "thumbnail": "https://kick.com/img/default-thumbnail-pictures/default2.jpeg",
        "url": "rtmps://stream.kick.com/1234567890",
        "viewer_count": 67
      },
      "stream_title": "My first stream"
    }
  ],
  "message": "OK"
}
```

---

### Update Channel

**Endpoint:** `PATCH https://api.kick.com/public/v1/channels`

**Authorization:** User Access Token with `channel:write` scope

**Request Body:**
```json
{
  "category_id": 123,
  "custom_tags": ["pog", "chad"],
  "stream_title": "My first stream"
}
```

**Success Response:** `204 No Content`

---

## Channel Rewards API

### Get Channel Rewards

**Endpoint:** `GET https://api.kick.com/public/v1/channels/rewards`

**Required Scopes:** `channel:rewards:read` or `channel:rewards:write`

**Success Response (200):**
```json
{
  "data": [
    {
      "background_color": "#00e701",
      "cost": 100,
      "description": "Request a song by providing a URL",
      "id": "01HZ8X9K2M4N6P8Q0R2S4T6V8W0Y2Z4",
      "is_enabled": true,
      "is_paused": false,
      "is_user_input_required": false,
      "should_redemptions_skip_request_queue": false,
      "title": "Song Request"
    }
  ],
  "message": "OK"
}
```

---

### Create Channel Reward

**Endpoint:** `POST https://api.kick.com/public/v1/channels/rewards`

**Required Scope:** `channel:rewards:write`

**Request Body:**
```json
{
  "background_color": "#fff111",
  "cost": 100,
  "description": "Request a song by providing a URL",
  "is_enabled": true,
  "is_user_input_required": false,
  "should_redemptions_skip_request_queue": false,
  "title": "Song Request"
}
```

**Constraints:**
- Max 15 rewards per channel (enabled + disabled)
- `title`: max 50 characters
- `description`: max 200 characters
- `cost`: minimum 1
- `background_color`: hex color (e.g., `#00e701`)

**Success Response (200):**
```json
{
  "data": {
    "background_color": "#00e701",
    "cost": 100,
    "description": "Request a song by providing a URL",
    "id": "01HZ8X9K2M4N6P8Q0R2S4T6V8W0Y2Z4",
    "is_enabled": true,
    "is_paused": false,
    "is_user_input_required": false,
    "should_redemptions_skip_request_queue": false,
    "title": "Song Request"
  },
  "message": "OK"
}
```

---

### Update Channel Reward

**Endpoint:** `PATCH https://api.kick.com/public/v1/channels/rewards/:id`

**Required Scope:** `channel:rewards:write`

**⚠️ Note:** Only the app that created the reward can update it

**Path Parameters:**
- `id` (ULID): Reward ID

**Request Body (all fields optional):**
```json
{
  "background_color": "#00e701",
  "cost": 100,
  "description": "Updated description",
  "is_enabled": true,
  "is_paused": false,
  "is_user_input_required": false,
  "should_redemptions_skip_request_queue": false,
  "title": "Updated Title"
}
```

**Success Response (200):** Returns updated reward object

---

### Delete Channel Reward

**Endpoint:** `DELETE https://api.kick.com/public/v1/channels/rewards/:id`

**Required Scope:** `channel:rewards:write`

**⚠️ Note:** Only the app that created the reward can delete it

**Path Parameters:**
- `id` (ULID): Reward ID

**Success Response:** `204 No Content`

---

## Chat API

### Post Chat Message

**Endpoint:** `POST https://api.kick.com/public/v1/chat`

**Required Scope:** `chat:write`

**Description:** Send a chat message as a user or bot

**Message Types:**
- **User:** Requires `broadcaster_user_id`, sends as authenticated user
- **Bot:** Ignores `broadcaster_user_id`, sends to channel attached to token

**Request Body:**
```json
{
  "broadcaster_user_id": 123,
  "content": "Pog",
  "reply_to_message_id": "828f83bb-e391-4c78-9e91-af35a172840e",
  "type": "user"
}
```

**Fields:**
- `broadcaster_user_id` (int, optional): Target channel (required for `type: user`)
- `content` (string, required, max 500 chars): Message text
- `reply_to_message_id` (UUID, optional): ID of message to reply to
- `type` (enum, required): `"user"` or `"bot"`

**Success Response (200):**
```json
{
  "data": {
    "is_sent": true,
    "message_id": "828f83bb-e391-4c78-9e91-af35a172840e"
  },
  "message": "OK"
}
```

**Error Responses:**
- `400` Bad Request
- `401` Unauthorized
- `403` Forbidden
- `404` Not Found (invalid broadcaster_user_id)
- `429` Too Many Requests (rate limit)

---

### Delete Chat Message

**Endpoint:** `DELETE https://api.kick.com/public/v1/chat/:message_id`

**Required Scope:** `moderation:chat_message:manage`

**Path Parameters:**
- `message_id` (string): Message ID to delete

**Success Response:** `204 No Content`

---

## Moderation API

### Ban/Timeout User

**Endpoint:** `POST https://api.kick.com/public/v1/moderation/bans`

**Required Scope:** `moderation:ban`

**Request Body:**
```json
{
  "broadcaster_user_id": 123,
  "duration": 10,
  "reason": "Not vibing",
  "user_id": 333
}
```

**Fields:**
- `broadcaster_user_id` (int, required): Channel to ban from
- `user_id` (int, required): User to ban
- `duration` (int, optional, 1-10080 minutes): Timeout duration (omit for permanent ban)
- `reason` (string, optional, max 100 chars): Ban reason

**Ban vs Timeout:**
- **Permanent Ban:** Omit `duration` field
- **Timeout:** Include `duration` in minutes (max 7 days = 10080 minutes)

**Success Response (200):**
```json
{
  "data": {},
  "message": "OK"
}
```

---

### Unban User

**Endpoint:** `DELETE https://api.kick.com/public/v1/moderation/bans`

**Required Scope:** `moderation:ban`

**Request Body:**
```json
{
  "broadcaster_user_id": 123,
  "user_id": 333
}
```

**Success Response (200):**
```json
{
  "data": {},
  "message": "OK"
}
```

---

## Livestreams API

### Get Livestreams

**Endpoint:** `GET https://api.kick.com/public/v1/livestreams`

**Authorization:** User Access Token

**Query Parameters:**
- `broadcaster_user_id` (int64[], max 50): Filter by broadcaster user IDs
- `category_id` (int64): Filter by category
- `language` (string): Filter by language code
- `limit` (int, 1-100): Max results to return
- `sort` (enum): `viewer_count` | `started_at`

**Example Request:**
```http
GET /public/v1/livestreams?category_id=101&limit=10&sort=viewer_count HTTP/1.1
Host: api.kick.com
Authorization: Bearer YOUR_OAUTH2_TOKEN
```

**Success Response (200):**
```json
{
  "data": [
    {
      "broadcaster_user_id": 123,
      "category": {
        "id": 101,
        "name": "Old School Runescape",
        "thumbnail": "https://kick.com/img/categories/old-school-runescape.jpeg"
      },
      "channel_id": 456,
      "custom_tags": ["tag1", "tag2"],
      "has_mature_content": true,
      "language": "en",
      "slug": "john-doe",
      "started_at": "0001-01-01T00:00:00Z",
      "stream_title": "My first stream",
      "thumbnail": "https://kick.com/img/default-thumbnail-pictures/default2.jpeg",
      "viewer_count": 73
    }
  ],
  "message": "OK"
}
```

---

### Get Livestreams Stats

**Endpoint:** `GET https://api.kick.com/public/v1/livestreams/stats`

**Authorization:** User Access Token

**Success Response (200):**
```json
{
  "data": {
    "total_count": 100
  },
  "message": "OK"
}
```

---

## Public Key API

### Get Public Key

**Endpoint:** `GET https://api.kick.com/public/v1/public-key`

**Authorization:** None required

**Description:** Retrieve the public key used for verifying webhook signatures

**Success Response (200):**
```json
{
  "data": {
    "public_key": "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAq/+l1WnlRrGSolDMA+A8\n6rAhMbQGmQ2SapVcGM3zq8ANXjnhDWocMqfWcTd95btDydITa10kDvHzw9WQOqp2\nMZI7ZyrfzJuz5nhTPCiJwTwnEtWft7nV14BYRDHvlfqPUaZ+1KR4OCaO/wWIk/rQ\nL/TjY0M70gse8rlBkbo2a8rKhu69RQTRsoaf4DVhDPEeSeI5jVrRDGAMGL3cGuyY\n6CLKGdjVEM78g3JfYOvDU/RvfqD7L89TZ3iN94jrmWdGz34JNlEI5hqK8dd7C5EF\nBEbZ5jgB8s8ReQV8H+MkuffjdAj3ajDDX3DOJMIut1lBrUVD1AaSrGCKHooWoL2e\ntwIDAQAB\n-----END PUBLIC KEY-----"
  },
  "message": "OK"
}
```

---

## KICKs (Gifting) API

### Get KICKs Leaderboard

**Endpoint:** `GET https://api.kick.com/public/v1/kicks/leaderboard`

**Required Scope:** `kicks:read`

**Query Parameters:**
- `top` (int, 1-100, optional): Number of top entries to return (e.g., 10 = top 10)

**Success Response (200):**
```json
{
  "data": {
    "lifetime": [
      {
        "gifted_amount": 100,
        "rank": 1,
        "user_id": 123,
        "username": "john-doe"
      }
    ],
    "month": [
      {
        "gifted_amount": 100,
        "rank": 1,
        "user_id": 123,
        "username": "john-doe"
      }
    ],
    "week": [
      {
        "gifted_amount": 100,
        "rank": 1,
        "user_id": 123,
        "username": "john-doe"
      }
    ]
  },
  "message": "OK"
}
```

---

## Webhooks & Events

### Introduction

**Webhooks** provide real-time event delivery from Kick to your application via HTTP POST requests.

**Key Features:**
- Real-time notifications for chat, follows, subscriptions, etc.
- Server-to-server POST requests
- Cryptographic signature verification
- Automatic retry on failure
- Subscription management via API

---

### Setup Webhooks

**Location:** [Kick Developer Settings](https://kick.com/settings/developer)

**Steps:**
1. Edit your Kick application
2. Find "Enable Webhooks" section
3. Toggle switch to **On**
4. Enter your **Webhook URL** (must be publicly accessible)

**⚠️ Important:** Webhook URL must be accessible over public internet
- ❌ `http://localhost:3000` won't work
- ✅ Use ngrok, Cloudflare Tunnel, or similar services

**Example Webhook URL:**
```
https://your-ngrok-url.ngrok-free.dev/webhooks/kick
```

---

### Webhook Headers

Every webhook request includes these headers:

| Header | Description | Example |
|--------|-------------|---------|
| `Kick-Event-Type` | Event type identifier | `chat.message.sent` |
| `Kick-Event-Version` | Event version | `1` |
| `Kick-Event-Signature` | Base64 signature (verify sender) | `SGVsbG8gV29ybGQ=` |
| `Kick-Event-Message-Id` | Unique message ID (idempotency) | `uuid` |
| `Kick-Event-Message-Timestamp` | Unix timestamp | `1638360000` |

---

### Webhook Security (Signature Verification)

**Why:** Prevents spoofed events from malicious actors

**Public Key Location:**
- Endpoint: `GET https://api.kick.com/public/v1/public-key`
- Hardcoded in docs (provided below)

**Kick Public Key:**
```
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAq/+l1WnlRrGSolDMA+A8
6rAhMbQGmQ2SapVcGM3zq8ANXjnhDWocMqfWcTd95btDydITa10kDvHzw9WQOqp2
MZI7ZyrfzJuz5nhTPCiJwTwnEtWft7nV14BYRDHvlfqPUaZ+1KR4OCaO/wWIk/rQ
L/TjY0M70gse8rlBkbo2a8rKhu69RQTRsoaf4DVhDPEeSeI5jVrRDGAMGL3cGuyY
6CLKGdjVEM78g3JfYOvDU/RvfqD7L89TZ3iN94jrmWdGz34JNlEI5hqK8dd7C5EF
BEbZ5jgB8s8ReQV8H+MkuffjdAj3ajDDX3DOJMIut1lBrUVD1AaSrGCKHooWoL2e
twIDAQAB
-----END PUBLIC KEY-----
```

**Signature Creation (by Kick):**
```
signature_string = "{message_id}.{timestamp}.{request_body}"
signature = RSA_SHA256_Sign(signature_string, kick_private_key)
Kick-Event-Signature = Base64Encode(signature)
```

**Verification Steps (your app):**
1. Extract headers: `Kick-Event-Signature`, `Kick-Event-Message-Id`, `Kick-Event-Message-Timestamp`
2. Read raw request body
3. Construct signature string: `"{message_id}.{timestamp}.{body}"`
4. Decode signature from Base64
5. Verify using Kick public key + SHA256
6. If valid → Process event
7. If invalid → Reject with 401 Unauthorized

**C# Example:**
```csharp
using System.Security.Cryptography;
using System.Text;

bool VerifySignature(string signature, string messageId, string timestamp, string body)
{
    var signatureBytes = Convert.FromBase64String(signature);
    var signatureString = $"{messageId}.{timestamp}.{body}";
    var signatureStringBytes = Encoding.UTF8.GetBytes(signatureString);
    
    using var rsa = RSA.Create();
    rsa.ImportFromPem(kickPublicKey);
    
    return rsa.VerifyData(
        signatureStringBytes,
        signatureBytes,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1
    );
}
```

---

### Webhook Retry Logic

**Behavior:**
- Kick sends webhook up to **3 times** if non-200 response
- Exponential backoff between retries

**Best Practice:**
- Respond with `200 OK` **immediately**
- Process event asynchronously (background queue)

**⚠️ Auto-Disable:**
After threshold of errors, Kick may automatically unsubscribe your app from webhooks. You must resubscribe via API.

---

### Subscribe to Events

**Endpoint:** `POST https://api.kick.com/public/v1/events/subscriptions`

**Required Scope:** `events:subscribe`

**Request Body:**
```json
{
  "broadcaster_user_id": 123,
  "events": [
    {
      "name": "chat.message.sent",
      "version": 1
    },
    {
      "name": "channel.followed",
      "version": 1
    }
  ],
  "method": "webhook"
}
```

**Fields:**
- `broadcaster_user_id` (int, optional):
  - **User Access Token:** Ignored (uses token's user ID)
  - **App Access Token:** Required
- `events` (array, required): List of events to subscribe to
  - `name` (string): Event type (see table below)
  - `version` (int): Event version (always `1`)
- `method` (enum): `"webhook"` (only option currently)

**Success Response (200):**
```json
{
  "data": [
    {
      "name": "chat.message.sent",
      "version": 1,
      "subscription_id": "01HZ8X9K2M4N6P8Q0R2S4T6V8W0Y2Z4",
      "error": null
    },
    {
      "name": "channel.followed",
      "version": 1,
      "subscription_id": "01HZ8X9K2M4N6P8Q0R2S4T6V8W0Y2Z5",
      "error": null
    }
  ],
  "message": "OK"
}
```

**Subscription Limits:**
- **Max 10,000 subscriptions per event type** per app
- **Max 1,000 chat.message.sent subscriptions** for unverified apps

---

### Get Active Subscriptions

**Endpoint:** `GET https://api.kick.com/public/v1/events/subscriptions`

**Authorization:** User Access Token with `events:subscribe` scope

**Query Parameters:**
- `broadcaster_user_id` (int64, optional): Filter by broadcaster

**Success Response (200):**
```json
{
  "data": [
    {
      "app_id": "01J9K2M4N6P8Q0R2S4T6V8W0Y2Z4A6B8",
      "broadcaster_user_id": 123,
      "created_at": "2021-01-01T00:00:00Z",
      "event": "chat.message.sent",
      "id": "01HZ8X9K2M4N6P8Q0R2S4T6V8W0Y2Z4",
      "method": "webhook",
      "updated_at": "2021-01-01T00:00:00Z",
      "version": 1
    }
  ],
  "message": "OK"
}
```

---

### Unsubscribe from Events

**Endpoint:** `DELETE https://api.kick.com/public/v1/events/subscriptions`

**Required Scope:** `events:subscribe`

**Query Parameters:**
- `id` (string[], required): Array of subscription IDs to delete

**Example:**
```http
DELETE /public/v1/events/subscriptions?id=sub123&id=sub456 HTTP/1.1
Host: api.kick.com
Authorization: Bearer YOUR_OAUTH2_TOKEN
```

**Success Response:** `204 No Content`

---

### Event Types

| Event Name | Kick-Event-Type Header | Description |
|------------|------------------------|-------------|
| Chat Message | `chat.message.sent` | User sends a chat message |
| Follow | `channel.followed` | User follows the channel |
| New Subscription | `channel.subscription.new` | User subscribes for first time |
| Subscription Renewal | `channel.subscription.renewal` | Existing subscription renews |
| Gifted Subscriptions | `channel.subscription.gifts` | User gifts subs to others |
| Reward Redemption | `channel.reward.redemption.updated` | Channel reward redeemed |
| Stream Start/End | `livestream.status.updated` | Stream goes live or ends |
| Stream Metadata | `livestream.metadata.updated` | Title/category/tags updated |
| User Banned | `moderation.banned` | User banned or timed out |
| KICKs Gifted | `kicks.gifted` | User sends a KICK gift |

---

### Event Payloads

#### Chat Message Event

**Headers:**
```
Kick-Event-Type: chat.message.sent
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "message_id": "unique_message_id_123",
  "broadcaster": {
    "is_anonymous": false,
    "user_id": 123456789,
    "username": "broadcaster_name",
    "is_verified": true,
    "profile_picture": "https://example.com/broadcaster_avatar.jpg",
    "channel_slug": "broadcaster_channel",
    "identity": null
  },
  "sender": {
    "is_anonymous": false,
    "user_id": 987654321,
    "username": "sender_name",
    "is_verified": false,
    "profile_picture": "https://example.com/sender_avatar.jpg",
    "channel_slug": "sender_channel",
    "identity": {
      "username_color": "#FF5733",
      "badges": [
        {
          "text": "Moderator",
          "type": "moderator"
        },
        {
          "text": "Subscriber",
          "type": "subscriber",
          "count": 3
        },
        {
          "text": "Sub Gifter",
          "type": "sub_gifter",
          "count": 5
        }
      ]
    }
  },
  "content": "Hello [emote:4148074:HYPERCLAP] [emote:37226:KEKW]",
  "emotes": [
    {
      "emote_id": "4148074",
      "positions": [
        { "s": 6, "e": 30 },
        { "s": 32, "e": 56 }
      ]
    },
    {
      "emote_id": "37226",
      "positions": [
        { "s": 58, "e": 75 }
      ]
    }
  ],
  "replies_to": {
    "message_id": "unique_message_id_456",
    "content": "This is the parent message!",
    "sender": {
      "is_anonymous": false,
      "user_id": 12345,
      "username": "parent_sender_name",
      "is_verified": false,
      "profile_picture": "https://example.com/parent_sender_avatar.jpg",
      "channel_slug": "parent_sender_channel",
      "identity": null
    }
  },
  "created_at": "2025-01-14T16:08:06Z"
}
```

**Badge Types:**
- `moderator` - Channel moderator
- `subscriber` - Subscriber (includes `count` = months)
- `owner` - Channel owner/broadcaster
- `verified` - Verified user
- `sub_gifter` - Subscription gifter (includes `count`)

---

#### Channel Follow Event

**Headers:**
```
Kick-Event-Type: channel.followed
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "broadcaster": {
    "is_anonymous": false,
    "user_id": 123456789,
    "username": "broadcaster_name",
    "is_verified": true,
    "profile_picture": "https://example.com/broadcaster_avatar.jpg",
    "channel_slug": "broadcaster_channel",
    "identity": null
  },
  "follower": {
    "is_anonymous": false,
    "user_id": 987654321,
    "username": "follower_name",
    "is_verified": false,
    "profile_picture": "https://example.com/sender_avatar.jpg",
    "channel_slug": "follower_channel",
    "identity": null
  }
}
```

---

#### Channel Subscription New

**Headers:**
```
Kick-Event-Type: channel.subscription.new
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "broadcaster": { "..." },
  "subscriber": { "..." },
  "duration": 1,
  "created_at": "2025-01-14T16:08:06Z",
  "expires_at": "2025-02-14T16:08:06Z"
}
```

---

#### Channel Subscription Renewal

**Headers:**
```
Kick-Event-Type: channel.subscription.renewal
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "broadcaster": { "..." },
  "subscriber": { "..." },
  "duration": 3,
  "created_at": "2025-01-14T16:08:06Z",
  "expires_at": "2025-02-14T16:08:06Z"
}
```

---

#### Gifted Subscriptions

**Headers:**
```
Kick-Event-Type: channel.subscription.gifts
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "broadcaster": { "..." },
  "gifter": {
    "is_anonymous": false,
    "user_id": 987654321,
    "username": "gifter_name",
    "is_verified": false,
    "profile_picture": "https://example.com/sender_avatar.jpg",
    "channel_slug": "gifter_channel",
    "identity": null
  },
  "giftees": [
    {
      "is_anonymous": false,
      "user_id": 561654654,
      "username": "giftee_name",
      "is_verified": true,
      "profile_picture": "https://example.com/broadcaster_avatar.jpg",
      "channel_slug": "giftee_channel",
      "identity": null
    }
  ],
  "created_at": "2025-01-14T16:08:06Z",
  "expires_at": "2025-02-14T16:08:06Z"
}
```

**Note:** `gifter` properties can be `null` if `is_anonymous: true`

---

#### Reward Redemption Updated

**Headers:**
```
Kick-Event-Type: channel.reward.redemption.updated
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "id": "01KBHE78QE4HZY1617DK5FC7YD",
  "user_input": "unban me",
  "status": "rejected",
  "redeemed_at": "2025-12-02T22:54:19.323Z",
  "reward": {
    "id": "01KBHE7RZNHB0SKDV1H86CD4F3",
    "title": "Uban Request",
    "cost": 1000,
    "description": "Only good reasons pls"
  },
  "redeemer": {
    "user_id": 123,
    "username": "naughty-user",
    "is_verified": false,
    "profile_picture": "",
    "channel_slug": "naughty_user"
  },
  "broadcaster": {
    "user_id": 333,
    "username": "gigachad",
    "is_verified": true,
    "profile_picture": "",
    "channel_slug": "gigachad"
  }
}
```

**Status Values:**
- `pending` - Waiting for approval
- `accepted` - Approved by broadcaster
- `rejected` - Denied by broadcaster

---

#### Livestream Status Updated

**Headers:**
```
Kick-Event-Type: livestream.status.updated
Kick-Event-Version: 1
```

**Stream Started:**
```json
{
  "broadcaster": { "..." },
  "is_live": true,
  "title": "Stream Title",
  "started_at": "2025-01-01T11:00:00+11:00",
  "ended_at": null
}
```

**Stream Ended:**
```json
{
  "broadcaster": { "..." },
  "is_live": false,
  "title": "Stream Title",
  "started_at": "2025-01-01T11:00:00+11:00",
  "ended_at": "2025-01-01T15:00:00+11:00"
}
```

---

#### Livestream Metadata Updated

**Headers:**
```
Kick-Event-Type: livestream.metadata.updated
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "broadcaster": { "..." },
  "metadata": {
    "title": "Stream Title",
    "language": "en",
    "has_mature_content": true,
    "category": {
      "id": 123,
      "name": "Category name",
      "thumbnail": "http://example.com/image123"
    }
  }
}
```

---

#### Moderation Banned

**Headers:**
```
Kick-Event-Type: moderation.banned
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "broadcaster": { "..." },
  "moderator": { "..." },
  "banned_user": {
    "is_anonymous": false,
    "user_id": 135790135,
    "username": "banned_user_name",
    "is_verified": false,
    "profile_picture": "https://example.com/banned_user_avatar.jpg",
    "channel_slug": "banned_user_channel",
    "identity": null
  },
  "metadata": {
    "reason": "banned reason",
    "created_at": "2025-01-14T16:08:05Z",
    "expires_at": "2025-01-14T16:10:06Z"
  }
}
```

**Note:** `expires_at` is `null` for permanent bans

---

#### KICKs Gifted

**Headers:**
```
Kick-Event-Type: kicks.gifted
Kick-Event-Version: 1
```

**Payload:**
```json
{
  "broadcaster": {
    "user_id": 123456789,
    "username": "broadcaster_name",
    "is_verified": true,
    "profile_picture": "https://example.com/broadcaster_avatar.jpg",
    "channel_slug": "broadcaster_channel"
  },
  "sender": {
    "user_id": 987654321,
    "username": "gift_sender",
    "is_verified": false,
    "profile_picture": "https://example.com/sender_avatar.jpg",
    "channel_slug": "gift_sender_channel"
  },
  "gift": {
    "amount": 500,
    "name": "Rage Quit",
    "type": "LEVEL_UP",
    "tier": "MID",
    "message": "w",
    "pinned_time_seconds": 600
  },
  "created_at": "2025-10-20T04:00:08.634Z"
}
```

---

## Organizations & Drops

**⚠️ Note:** Organizations and Drops are advanced features primarily for game developers and studios. They are **NOT required** for basic bot/chat integration.

### Organizations

**Purpose:** Manage developer activities as a team

**Use Cases:**
- Running Drops campaigns
- Managing multiple team members
- (Future) Managing OAuth apps as a group

**Registration:** Email `developers@kick.com` with:
- Organization name
- Organization URL
- Logo URL
- Stream category to claim
- Kick usernames of members (all must have 2FA enabled)
- OAuth app client ID
- Webhook URL
- Connection page URL

---

### Drops

**Purpose:** Reward viewers for watching streams with in-game items

**Who Can Use:** Organization owners only

**How It Works:**
1. Create campaign on [dev.kick.com](https://dev.kick.com/)
2. Viewers watch participating streams
3. Viewers link Kick + game accounts
4. Viewers claim rewards via Inventory page
5. Your server receives webhook when reward claimed

**Webhook Payload (Reward Claimed):**
```json
{
  "claim_id": "01JAXK8N4QWRTY5PM7ZEBVJDS8S",
  "user_id": 1234567890,
  "campaign_id": "01JAXK8N4QWRTY5PM7ZEBVJDS8S",
  "reward_id": "01JAXK8N4QWRTY5PM7ZEBVJDS8S",
  "external_id": "your_game_item_id"
}
```

**Best Practices:**
- Respond with `200 OK` immediately
- Use `claim_id` for idempotency (don't duplicate rewards)
- Verify webhook signature
- Max 12 rewards per campaign

---

## Additional Resources

### Official Links
- **Developer Portal:** https://dev.kick.com
- **Documentation:** https://docs.kick.com
- **GitHub (Dev Docs):** https://github.com/KickEngineering/KickDevDocs
- **Developer Discord:** https://discord.gg/SvyWXP5aWb
- **Support Email:** developers@kick.com

### Changelog
- **Dec 2, 2025:** Added delete app functionality
- **Dec 2, 2025:** Added DELETE /chat/:message_id endpoint
- **Nov 27, 2025:** Added pinned_time_seconds to kicks.gifted event
- **Nov 25, 2025:** Made refresh tokens reusable/flexible
- **Nov 25, 2025:** Added tags and viewer_count to GET /categories/:id
- **Nov 21, 2025:** Added custom_tags to GET /livestreams
- **Nov 21, 2025:** Added custom_tags to GET /channels
- **Oct 27, 2025:** Added GET /kicks/leaderboard endpoint
- **Oct 21, 2025:** Added kicks.gifted webhook event

---

**End of Kick API Complete Documentation**

